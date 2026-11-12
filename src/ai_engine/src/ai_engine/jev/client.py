"""Jev (TypeSafe) Decisions API client — typed Noul screens via OpenRouter."""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from typing import Any

import httpx

from ai_engine.core.config import Settings, get_settings
from ai_engine.core.retry import call_with_backoff, retry_after_from_headers

logger = logging.getLogger(__name__)

_JEV_ATTEMPTS = 4
_RETRYABLE_STATUS = {408, 409, 429, 500, 502, 503, 504}


@dataclass(frozen=True)
class JevNoulQuestion:
    key: str
    req_type: str
    instructions: str


@dataclass(frozen=True)
class JevScreenResult:
    used: bool
    model: str | None = None
    noul_by_key: dict[str, float] = field(default_factory=dict)

    @property
    def certainty(self) -> float | None:
        return jev_certainty(self.noul_by_key)


def jev_certainty(noul_by_key: dict[str, float]) -> float | None:
    if not noul_by_key:
        return None
    values = [abs(float(noul) - 0.5) * 2 for noul in noul_by_key.values()]
    return round(sum(values) / len(values), 3)


def blend_confidence(claude_confidence: float, jev_certainty_value: float | None) -> float:
    if jev_certainty_value is None:
        return claude_confidence
    return round(min(claude_confidence, jev_certainty_value), 3)


def hard_fail_keys(
    questions: list[JevNoulQuestion],
    noul_by_key: dict[str, float],
    *,
    yes_threshold: float,
    no_threshold: float,
) -> set[str]:
    failed: set[str] = set()
    for question in questions:
        noul = noul_by_key.get(question.key)
        if noul is None:
            continue
        disqualify = question.req_type == "DISQUALIFYING" and noul >= yes_threshold
        mandatory_miss = question.req_type == "MANDATORY" and noul <= no_threshold
        if disqualify or mandatory_miss:
            failed.add(question.key)
    return failed


def is_retryable_jev_error(exc: BaseException) -> bool:
    if isinstance(exc, httpx.TimeoutException | httpx.NetworkError):
        return True
    if isinstance(exc, httpx.HTTPStatusError):
        return exc.response.status_code in _RETRYABLE_STATUS
    return False


class JevClient:
    def __init__(
        self,
        settings: Settings | None = None,
        http: httpx.AsyncClient | None = None,
    ) -> None:
        self._settings = settings or get_settings()
        self._http = http

    async def screen(
        self, state: dict[str, Any], questions: list[JevNoulQuestion]
    ) -> JevScreenResult:
        if not self._settings.openrouter_api_key:
            return JevScreenResult(used=False)
        if not questions:
            return JevScreenResult(used=False)

        payload = {
            "model": self._settings.jev_model_id,
            "state": state,
            "questions": {
                q.key: {
                    "type": "noul",
                    "instructions": q.instructions,
                }
                for q in questions
            },
        }

        async def _once() -> dict[str, Any]:
            client = self._http
            owns = client is None
            if client is None:
                client = httpx.AsyncClient(timeout=30.0)
            try:
                response = await client.post(
                    self._settings.jev_decisions_url,
                    headers={
                        "Authorization": f"Bearer {self._settings.openrouter_api_key}",
                        "Content-Type": "application/json",
                    },
                    json=payload,
                )
                response.raise_for_status()
                data = response.json()
                if not isinstance(data, dict):
                    msg = "Jev response was not an object"
                    raise RuntimeError(msg)
                return data
            finally:
                if owns:
                    await client.aclose()

        body = await call_with_backoff(
            _once,
            is_retryable=is_retryable_jev_error,
            max_attempts=_JEV_ATTEMPTS,
            retry_after=lambda exc: retry_after_from_headers(
                getattr(getattr(exc, "response", None), "headers", None)
            ),
        )
        answers = body.get("answers") or {}
        noul_by_key: dict[str, float] = {}
        for question in questions:
            raw = answers.get(question.key) or {}
            if "noul" not in raw:
                continue
            noul_by_key[question.key] = max(0.0, min(1.0, float(raw["noul"])))
        return JevScreenResult(
            used=True,
            model=str(body.get("model") or self._settings.jev_model_id),
            noul_by_key=noul_by_key,
        )
