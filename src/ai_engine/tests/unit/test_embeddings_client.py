from unittest.mock import AsyncMock, MagicMock

import pytest

from ai_engine.core.config import Settings
from ai_engine.rag.embeddings.client import EmbeddingsClient


def _vector(dim: int = 1024) -> list[float]:
    return [0.01] * dim


@pytest.mark.asyncio
async def test_embed_texts_primary_success(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = Settings(
        openrouter_api_key="or-key",
        embeddings_dimensions=1024,
        embeddings_model="nvidia/nv-embed-v1",
        embeddings_model_fallback="voyage/voyage-3-lite",
    )
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
async def test_embed_texts_falls_back_to_voyage(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = Settings(
        openrouter_api_key="or-key",
        embeddings_dimensions=1024,
        embeddings_model="nvidia/nv-embed-v1",
        embeddings_model_fallback="voyage/voyage-3-lite",
    )
    monkeypatch.setattr("ai_engine.rag.embeddings.client.get_settings", lambda: settings)

    response = MagicMock()
    response.data = [MagicMock(embedding=_vector())]

    openrouter = MagicMock()
    openrouter.embeddings.create = AsyncMock(side_effect=[RuntimeError("rate limit"), response])

    client = EmbeddingsClient(settings)
    client._openrouter = openrouter

    batch = await client.embed_texts(["hello"])
    assert batch.provider == "voyage"
    assert batch.model == "voyage/voyage-3-lite"
    assert openrouter.embeddings.create.await_count == 2


@pytest.mark.asyncio
async def test_embed_texts_requires_openrouter_key() -> None:
    client = EmbeddingsClient(Settings(openrouter_api_key=""))
    with pytest.raises(RuntimeError, match="OPENROUTER_API_KEY"):
        await client.embed_texts(["x"])
