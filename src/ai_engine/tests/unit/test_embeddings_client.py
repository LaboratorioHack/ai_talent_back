from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest
from openai import RateLimitError

from ai_engine.core.config import Settings
from ai_engine.core.retry import bounded_retry_delay
from ai_engine.rag.embeddings.client import (
    EmbeddingsClient,
    is_daily_quota,
    is_embedding_rate_limit,
    is_retryable_embedding_error,
    reset_nvidia_circuit,
)


def _vector(dim: int = 1024) -> list[float]:
    return [0.01] * dim


def _settings() -> Settings:
    return Settings(
        openrouter_api_key="or-key",
        embeddings_dimensions=1024,
        embeddings_model="nvidia/nemotron-3.5-embed-1b:free",
        embeddings_model_fallback="mongodb/voyage-4-lite",
    )


def _openai_429(*, retry_after: str | None = "0") -> RateLimitError:
    request = httpx.Request("POST", "https://openrouter.ai/api/v1/embeddings")
    headers = {"retry-after": retry_after} if retry_after is not None else {}
    response = httpx.Response(429, request=request, headers=headers)
    return RateLimitError(
        "Error code: 429 - Rate limit exceeded: free-models-per-min",
        response=response,
        body=None,
    )


def _ok_response() -> MagicMock:
    response = MagicMock()
    response.data = [MagicMock(embedding=_vector())]
    return response


@pytest.fixture(autouse=True)
def _reset_circuit() -> None:
    reset_nvidia_circuit()
    yield
    reset_nvidia_circuit()


def test_bounded_retry_delay_floors_5_10_15() -> None:
    floors = (5.0, 10.0, 15.0)
    assert bounded_retry_delay(0, floors=floors, header=None) == 5.0
    assert bounded_retry_delay(1, floors=floors, header=None) == 10.0
    assert bounded_retry_delay(2, floors=floors, header=None) == 15.0


def test_bounded_retry_delay_honors_retry_after_max() -> None:
    floors = (5.0, 10.0, 15.0)
    assert bounded_retry_delay(0, floors=floors, header=0.0) == 5.0
    assert bounded_retry_delay(0, floors=floors, header=12.0) == 12.0
    assert bounded_retry_delay(0, floors=floors, header=90.0, cap=60.0) == 60.0


def test_rate_limit_is_retryable_embedding() -> None:
    assert is_retryable_embedding_error(_openai_429()) is True
    assert is_embedding_rate_limit(_openai_429()) is True
    assert is_retryable_embedding_error(RuntimeError("model missing")) is False
    assert is_retryable_embedding_error(RuntimeError("rate limit")) is False


@pytest.mark.asyncio
async def test_embed_texts_primary_success(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = _settings()
    monkeypatch.setattr("ai_engine.rag.embeddings.client.get_settings", lambda: settings)

    response = MagicMock()
    response.data = [MagicMock(embedding=_vector()), MagicMock(embedding=_vector())]
    openrouter = MagicMock()
    openrouter.embeddings.create = AsyncMock(return_value=response)

    client = EmbeddingsClient(settings)
    client._openrouter = openrouter

    batch = await client.embed_texts(["a", "b"])
    assert batch.provider == "nvidia"
    assert len(batch.vectors) == 2
    openrouter.embeddings.create.assert_awaited_once()


@pytest.mark.asyncio
async def test_embed_texts_falls_back_to_voyage_on_non_retryable(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    settings = _settings()
    monkeypatch.setattr("ai_engine.rag.embeddings.client.get_settings", lambda: settings)

    openrouter = MagicMock()
    openrouter.embeddings.create = AsyncMock(
        side_effect=[RuntimeError("model missing"), _ok_response()]
    )

    client = EmbeddingsClient(settings)
    client._openrouter = openrouter

    batch = await client.embed_texts(["hello"])
    assert batch.provider == "voyage"
    assert batch.model == "mongodb/voyage-4-lite"
    assert openrouter.embeddings.create.await_count == 2


@pytest.mark.asyncio
async def test_embed_retries_rate_limit_on_primary_without_fallback(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    settings = _settings()
    monkeypatch.setattr("ai_engine.rag.embeddings.client.get_settings", lambda: settings)
    monkeypatch.setattr("ai_engine.core.retry.asyncio.sleep", AsyncMock())

    openrouter = MagicMock()
    openrouter.embeddings.create = AsyncMock(side_effect=[_openai_429(), _ok_response()])

    client = EmbeddingsClient(settings)
    client._openrouter = openrouter
    batch = await client.embed_texts(["hello"])
    assert batch.provider == "nvidia"
    assert openrouter.embeddings.create.await_count == 2


@pytest.mark.asyncio
async def test_embed_four_nvidia_429s_then_voyage(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = _settings()
    monkeypatch.setattr("ai_engine.rag.embeddings.client.get_settings", lambda: settings)
    sleeps = AsyncMock()
    monkeypatch.setattr("ai_engine.core.retry.asyncio.sleep", sleeps)

    openrouter = MagicMock()
    openrouter.embeddings.create = AsyncMock(
        side_effect=[_openai_429(retry_after=None)] * 4 + [_ok_response()]
    )

    client = EmbeddingsClient(settings)
    client._openrouter = openrouter
    batch = await client.embed_texts(["hello"])
    assert batch.provider == "voyage"
    assert openrouter.embeddings.create.await_count == 5
    assert sleeps.await_count == 3
    delays = [call.args[0] for call in sleeps.await_args_list]
    assert delays == [5.0, 10.0, 15.0]


@pytest.mark.asyncio
async def test_nvidia_circuit_skips_primary_after_429_exhaustion(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    settings = _settings()
    monkeypatch.setattr("ai_engine.rag.embeddings.client.get_settings", lambda: settings)
    monkeypatch.setattr("ai_engine.core.retry.asyncio.sleep", AsyncMock())

    openrouter = MagicMock()
    openrouter.embeddings.create = AsyncMock(
        side_effect=[_openai_429()] * 4 + [_ok_response(), _ok_response()]
    )

    client = EmbeddingsClient(settings)
    client._openrouter = openrouter
    first = await client.embed_texts(["one"])
    assert first.provider == "voyage"
    nvidia_calls = openrouter.embeddings.create.await_count
    second = await client.embed_texts(["two"])
    assert second.provider == "voyage"
    assert openrouter.embeddings.create.await_count == nvidia_calls + 1


@pytest.mark.asyncio
async def test_embed_texts_requires_openrouter_key() -> None:
    client = EmbeddingsClient(Settings(openrouter_api_key=""))
    with pytest.raises(RuntimeError, match="OPENROUTER_API_KEY"):
        await client.embed_texts(["x"])


def test_daily_quota_is_not_retried() -> None:
    exc = RuntimeError("Error code: 429 - Rate limit exceeded: free-models-per-day.")
    assert is_daily_quota(exc)
    assert not is_retryable_embedding_error(exc)
