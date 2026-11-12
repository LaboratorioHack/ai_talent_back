import json
from unittest.mock import AsyncMock

import httpx
import pytest

from ai_engine.core.config import Settings
from ai_engine.jev.client import (
    JevClient,
    JevNoulQuestion,
    blend_confidence,
    hard_fail_keys,
    is_retryable_jev_error,
    jev_certainty,
)


def _settings() -> Settings:
    return Settings(
        openrouter_api_key="or-key",
        jev_model_id="typesafe/jev-1.14",
        jev_decisions_url="https://openrouter.ai/api/alpha/decisions",
    )


def test_jev_certainty_and_blend() -> None:
    assert jev_certainty({}) is None
    assert jev_certainty({"a": 0.91}) == pytest.approx(0.82)
    assert jev_certainty({"a": 0.5}) == 0.0
    assert blend_confidence(0.9, None) == 0.9
    assert blend_confidence(0.9, 0.4) == 0.4


def test_hard_fail_keys() -> None:
    questions = [
        JevNoulQuestion("d", "DISQUALIFYING", "visa?"),
        JevNoulQuestion("m", "MANDATORY", "Go?"),
    ]
    assert hard_fail_keys(
        questions, {"d": 0.91, "m": 0.7}, yes_threshold=0.8, no_threshold=0.2
    ) == {"d"}
    assert hard_fail_keys(questions, {"d": 0.1, "m": 0.1}, yes_threshold=0.8, no_threshold=0.2) == {
        "m"
    }
    assert (
        hard_fail_keys(questions, {"d": 0.5, "m": 0.5}, yes_threshold=0.8, no_threshold=0.2)
        == set()
    )


def test_429_is_retryable() -> None:
    request = httpx.Request("POST", "https://openrouter.ai/api/alpha/decisions")
    response = httpx.Response(429, request=request)
    exc = httpx.HTTPStatusError("limited", request=request, response=response)
    assert is_retryable_jev_error(exc) is True
    assert is_retryable_jev_error(ValueError("nope")) is False


@pytest.mark.asyncio
async def test_screen_without_api_key_is_unused() -> None:
    client = JevClient(Settings(openrouter_api_key=""))
    result = await client.screen(
        {"summary": "x"},
        [JevNoulQuestion("req_a", "MANDATORY", "Go?")],
    )
    assert result.used is False
    assert result.noul_by_key == {}


@pytest.mark.asyncio
async def test_screen_parses_noul() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        body = json.loads(request.content)
        assert body["model"] == "typesafe/jev-1.14"
        assert "req_a" in body["questions"]
        return httpx.Response(
            200,
            json={
                "model": "typesafe/jev-1.14-dated",
                "answers": {"req_a": {"type": "noul", "noul": 0.91}},
            },
            request=request,
        )

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        result = await JevClient(_settings(), http=http).screen(
            {"summary": "Go services"},
            [JevNoulQuestion("req_a", "DISQUALIFYING", "visa issue?")],
        )
    assert result.used is True
    assert result.noul_by_key["req_a"] == 0.91
    assert result.model == "typesafe/jev-1.14-dated"
    assert result.certainty == pytest.approx(0.82)


@pytest.mark.asyncio
async def test_screen_retries_429(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr("ai_engine.core.retry.asyncio.sleep", AsyncMock())
    calls = {"n": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        calls["n"] += 1
        if calls["n"] == 1:
            return httpx.Response(429, request=request, headers={"retry-after": "0"})
        return httpx.Response(
            200,
            json={"model": "typesafe/jev-1.14", "answers": {"req_a": {"noul": 0.2}}},
            request=request,
        )

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        result = await JevClient(_settings(), http=http).screen(
            {"summary": "x"},
            [JevNoulQuestion("req_a", "MANDATORY", "Go?")],
        )
    assert calls["n"] == 2
    assert result.noul_by_key["req_a"] == 0.2
