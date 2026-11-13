"""Anthropic client for text and vision (CV OCR), with isolated 429 backoff."""

from __future__ import annotations

import base64
import json
import logging
from collections.abc import Awaitable, Callable
from typing import Any, TypeVar

import anthropic
from pydantic import BaseModel

from ai_engine.core.config import Settings, get_settings
from ai_engine.core.retry import call_with_backoff, headers_from_exc, retry_after_from_headers

logger = logging.getLogger(__name__)

T = TypeVar("T", bound=BaseModel)

_ANTHROPIC_ATTEMPTS = 5
_ANTHROPIC_BASE = 1.5


def is_retryable_anthropic_error(exc: BaseException) -> bool:
    if isinstance(
        exc, anthropic.RateLimitError | anthropic.APITimeoutError | anthropic.APIConnectionError
    ):
        return True
    if isinstance(exc, anthropic.APIStatusError):
        return exc.status_code >= 500
    return False


def _anthropic_retry_after(exc: BaseException) -> float | None:
    return retry_after_from_headers(headers_from_exc(exc))


class LlmClient:
    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()
        self._client = anthropic.AsyncAnthropic(
            api_key=self._settings.anthropic_api_key,
            max_retries=0,
        )

    @property
    def text_model(self) -> str:
        return self._settings.anthropic_model

    @property
    def vision_model(self) -> str:
        return self._settings.vision_model

    async def _with_retry(self, factory: Callable[[], Awaitable[Any]]) -> Any:
        return await call_with_backoff(
            factory,
            is_retryable=is_retryable_anthropic_error,
            max_attempts=_ANTHROPIC_ATTEMPTS,
            base=_ANTHROPIC_BASE,
            retry_after=_anthropic_retry_after,
        )

    async def complete_text(self, system: str, user: str, *, max_tokens: int = 4096) -> str:
        async def _once() -> Any:
            return await self._client.messages.create(
                model=self.text_model,
                max_tokens=max_tokens,
                system=system,
                messages=[{"role": "user", "content": user}],
            )

        message = await self._with_retry(_once)
        return _text_from_message(message)

    async def complete_chat(
        self,
        system: str,
        messages: list[dict[str, str]],
        *,
        max_tokens: int = 4096,
        effort: str = "low",
    ) -> str:
        """A multi-turn conversation (``role``/``content`` turns, the last one from the user).

        Current models think before answering; ``max_tokens`` must leave room for that, and a
        chat reply needs little of it (``effort``), so it stays fast and cheap.
        """

        async def _once() -> Any:
            return await self._client.messages.create(
                model=self.text_model,
                max_tokens=max_tokens,
                system=system,
                messages=messages,
                output_config={"effort": effort},
            )

        message = await self._with_retry(_once)
        text = _text_from_message(message)
        if not text:
            logger.warning("Chat reply without text (stop_reason=%s)", message.stop_reason)
        return text

    async def complete_json(
        self,
        system: str,
        user: str,
        model_type: type[T],
        *,
        max_tokens: int = 2048,
        tool_name: str = "extracted_profile",
        tool_description: str = "Structured professional profile extracted from the CV.",
    ) -> T:
        schema = model_type.model_json_schema()

        async def _once() -> Any:
            return await self._client.messages.create(
                model=self.text_model,
                max_tokens=max_tokens,
                system=system,
                tools=[
                    {
                        "name": tool_name,
                        "description": tool_description,
                        "input_schema": schema,
                    }
                ],
                tool_choice={"type": "tool", "name": tool_name},
                messages=[{"role": "user", "content": user}],
            )

        message = await self._with_retry(_once)
        data = _tool_input_from_message(message, tool_name)
        if data is None:
            data = _extract_json(_text_from_message(message))
        return model_type.model_validate(data)

    async def transcribe_page_image(
        self,
        png_bytes: bytes,
        *,
        page_number: int,
    ) -> tuple[str, float]:
        """Vision OCR for scanned/blurry PDF pages."""
        b64 = base64.standard_b64encode(png_bytes).decode("ascii")
        prompt = (
            f"Transcribe CV/resume page {page_number}. Preserve sections, bullet order, "
            "and two-column reading order top-to-bottom within each column. Output plain text only."
        )

        async def _once() -> Any:
            return await self._client.messages.create(
                model=self.vision_model,
                max_tokens=4096,
                messages=[
                    {
                        "role": "user",
                        "content": [
                            {
                                "type": "image",
                                "source": {
                                    "type": "base64",
                                    "media_type": "image/png",
                                    "data": b64,
                                },
                            },
                            {"type": "text", "text": prompt},
                        ],
                    }
                ],
            )

        message = await self._with_retry(_once)
        text = _text_from_message(message)
        confidence = 0.75 if len(text.strip()) > 80 else 0.45
        return text, confidence


def _text_from_message(message: Any) -> str:
    parts: list[str] = []
    for block in message.content:
        if getattr(block, "type", None) == "text":
            parts.append(block.text)
    return "\n".join(parts).strip()


def _tool_input_from_message(message: Any, tool_name: str) -> dict[str, Any] | None:
    for block in getattr(message, "content", []) or []:
        if getattr(block, "type", None) == "tool_use" and getattr(block, "name", "") == tool_name:
            payload = getattr(block, "input", None)
            if isinstance(payload, dict):
                return payload
    return None


def _extract_json(raw: str) -> dict[str, Any]:
    text = raw.strip()
    if text.startswith("```"):
        lines = text.split("\n")
        if lines[0].startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]
        text = "\n".join(lines)
    start = text.find("{")
    end = text.rfind("}")
    if start >= 0 and end > start:
        text = text[start : end + 1]
    return json.loads(text)
