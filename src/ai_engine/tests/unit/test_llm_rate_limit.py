from unittest.mock import AsyncMock, MagicMock

import anthropic
import httpx
import pytest

from ai_engine.core.config import Settings
from ai_engine.core.llm import LlmClient, is_retryable_anthropic_error


def _rate_limit() -> anthropic.RateLimitError:
    request = httpx.Request("POST", "https://api.anthropic.com/v1/messages")
    response = httpx.Response(429, request=request, headers={"retry-after": "0"})
    return anthropic.RateLimitError("rate limited", response=response, body=None)


def test_rate_limit_is_retryable() -> None:
    assert is_retryable_anthropic_error(_rate_limit()) is True
    assert is_retryable_anthropic_error(ValueError("nope")) is False


@pytest.mark.asyncio
async def test_complete_json_retries_rate_limit(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr("ai_engine.core.retry.asyncio.sleep", AsyncMock())
    message = MagicMock()
    block = MagicMock()
    block.type = "tool_use"
    block.name = "extracted_profile"
    block.input = {"summary": "Engineer"}
    message.content = [block]

    client = LlmClient(Settings(anthropic_api_key="k", anthropic_model="claude-sonnet-5-5"))
    client._client.messages.create = AsyncMock(side_effect=[_rate_limit(), message])

    from ai_engine.schemas.cv import StructuredCandidateProfile

    profile = await client.complete_json(
        "sys", "cv", StructuredCandidateProfile, max_tokens=2048
    )
    assert profile.summary == "Engineer"
    assert client._client.messages.create.await_count == 2
