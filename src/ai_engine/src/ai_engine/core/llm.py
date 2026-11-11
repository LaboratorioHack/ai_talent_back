"""Anthropic client for text and vision (CV OCR)."""

import base64
import json
import logging
from typing import Any, TypeVar

import anthropic
from pydantic import BaseModel

from ai_engine.core.config import Settings, get_settings

logger = logging.getLogger(__name__)

T = TypeVar("T", bound=BaseModel)


class LlmClient:
    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()
        self._client = anthropic.AsyncAnthropic(api_key=self._settings.anthropic_api_key)

    @property
    def text_model(self) -> str:
        return self._settings.anthropic_model

    @property
    def vision_model(self) -> str:
        return self._settings.vision_model

    async def complete_text(self, system: str, user: str, *, max_tokens: int = 4096) -> str:
        message = await self._client.messages.create(
            model=self.text_model,
            max_tokens=max_tokens,
            system=system,
            messages=[{"role": "user", "content": user}],
        )
        return _text_from_message(message)

    async def complete_json(
        self,
        system: str,
        user: str,
        model_type: type[T],
        *,
        max_tokens: int = 8192,
    ) -> T:
        schema = model_type.model_json_schema()
        instruction = (
            f"{system}\n\nRespond with a single JSON object matching this schema:\n"
            f"{json.dumps(schema, indent=2)}"
        )
        raw = await self.complete_text(instruction, user, max_tokens=max_tokens)
        data = _extract_json(raw)
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
        message = await self._client.messages.create(
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
        text = _text_from_message(message)
        confidence = 0.75 if len(text.strip()) > 80 else 0.45
        return text, confidence


def _text_from_message(message: Any) -> str:
    parts: list[str] = []
    for block in message.content:
        if getattr(block, "type", None) == "text":
            parts.append(block.text)
    return "\n".join(parts).strip()


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
