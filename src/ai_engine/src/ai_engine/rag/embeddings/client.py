"""Embeddings via OpenRouter: Nvidia primary, Voyage fallback (1024-dim for pgvector)."""

import logging
from dataclasses import dataclass

from openai import AsyncOpenAI

from ai_engine.core.config import Settings, get_settings

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class EmbeddingBatch:
    vectors: list[list[float]]
    model: str
    provider: str


class EmbeddingsClient:
    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()
        self._openrouter: AsyncOpenAI | None = None
        if self._settings.openrouter_api_key:
            self._openrouter = AsyncOpenAI(
                api_key=self._settings.openrouter_api_key,
                base_url=self._settings.openrouter_base_url,
            )

    async def embed_texts(self, texts: list[str]) -> EmbeddingBatch:
        if not texts:
            return EmbeddingBatch([], "", "")

        if self._openrouter is None:
            msg = "OPENROUTER_API_KEY is required for embeddings"
            raise RuntimeError(msg)

        errors: list[str] = []
        try:
            return await self._embed_openrouter(texts, self._settings.embeddings_model, "nvidia")
        except Exception as exc:  # noqa: BLE001
            logger.warning(
                "Primary embedding model failed (%s): %s",
                self._settings.embeddings_model,
                exc,
            )
            errors.append(str(exc))

        try:
            return await self._embed_openrouter(
                texts,
                self._settings.embeddings_model_fallback,
                "voyage",
            )
        except Exception as exc:  # noqa: BLE001
            msg = f"Embedding models failed: {errors + [str(exc)]}"
            raise RuntimeError(msg) from exc

    async def _embed_openrouter(
        self,
        texts: list[str],
        model: str,
        provider: str,
    ) -> EmbeddingBatch:
        assert self._openrouter is not None
        response = await self._openrouter.embeddings.create(model=model, input=texts)
        vectors = [_validate_dim(item.embedding) for item in response.data]
        return EmbeddingBatch(vectors=vectors, model=model, provider=provider)


def _validate_dim(vector: list[float]) -> list[float]:
    settings = get_settings()
    if len(vector) != settings.embeddings_dimensions:
        msg = (
            f"Expected embedding dim {settings.embeddings_dimensions}, got {len(vector)}. "
            "Check EMBEDDINGS_MODEL / EMBEDDINGS_MODEL_FALLBACK on OpenRouter."
        )
        raise ValueError(msg)
    return vector
