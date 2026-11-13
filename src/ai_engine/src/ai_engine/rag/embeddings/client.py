"""Embeddings via OpenRouter: Nvidia primary, Voyage fallback (1024-dim for pgvector)."""

from __future__ import annotations

import logging
import time
from dataclasses import dataclass

from openai import (
    APIConnectionError,
    APIStatusError,
    APITimeoutError,
    AsyncOpenAI,
    OpenAIError,
    RateLimitError,
)

from ai_engine.core.config import Settings, get_settings
from ai_engine.core.retry import (
    call_with_backoff,
    headers_from_exc,
    retry_after_from_headers,
)

logger = logging.getLogger(__name__)

_EMBED_ATTEMPTS = 4  # 1 try + 3 retries, then Voyage
_429_FLOORS = (5.0, 10.0, 15.0)
_NVIDIA_CIRCUIT_SECONDS = 60.0
# A daily quota (OpenRouter free models: "free-models-per-day") will not recover in a minute:
# retrying it only delays every embedding by the 5/10/15 s floors before Voyage answers.
_DAILY_QUOTA_CIRCUIT_SECONDS = 3600.0

_nvidia_circuit_until = 0.0
_nvidia_circuit_logged = False


@dataclass(frozen=True)
class EmbeddingBatch:
    vectors: list[list[float]]
    model: str
    provider: str


def _message_looks_like_rate_limit(exc: BaseException) -> bool:
    text = str(exc).lower()
    return "429" in text or "rate limit" in text


def is_embedding_rate_limit(exc: BaseException) -> bool:
    if isinstance(exc, RateLimitError):
        return True
    if isinstance(exc, APIStatusError) and exc.status_code == 429:
        return True
    return _message_looks_like_rate_limit(exc)


def is_daily_quota(exc: BaseException) -> bool:
    text = str(exc).lower()
    return "per-day" in text or "per day" in text


def is_retryable_embedding_error(exc: BaseException) -> bool:
    if is_daily_quota(exc):
        return False
    if isinstance(exc, RateLimitError | APITimeoutError | APIConnectionError):
        return True
    if isinstance(exc, APIStatusError):
        return exc.status_code in {408, 409, 429, 500, 502, 503, 504}
    return bool(isinstance(exc, OpenAIError) and _message_looks_like_rate_limit(exc))


def reset_nvidia_circuit() -> None:
    global _nvidia_circuit_until, _nvidia_circuit_logged
    _nvidia_circuit_until = 0.0
    _nvidia_circuit_logged = False


def nvidia_circuit_open() -> bool:
    global _nvidia_circuit_logged
    if time.monotonic() >= _nvidia_circuit_until:
        _nvidia_circuit_logged = False
        return False
    return True


def trip_nvidia_circuit(seconds: float = _NVIDIA_CIRCUIT_SECONDS) -> None:
    global _nvidia_circuit_until, _nvidia_circuit_logged
    _nvidia_circuit_until = time.monotonic() + seconds
    if not _nvidia_circuit_logged:
        logger.warning("Nvidia embedding circuit open (429); using Voyage fallback")
        _nvidia_circuit_logged = True


class EmbeddingsClient:
    def __init__(self, settings: Settings | None = None) -> None:
        self._settings = settings or get_settings()
        self._openrouter: AsyncOpenAI | None = None
        if self._settings.openrouter_api_key:
            self._openrouter = AsyncOpenAI(
                api_key=self._settings.openrouter_api_key,
                base_url=self._settings.openrouter_base_url,
                max_retries=0,
            )

    async def embed_texts(self, texts: list[str]) -> EmbeddingBatch:
        if not texts:
            return EmbeddingBatch([], "", "")

        if self._openrouter is None:
            msg = "OPENROUTER_API_KEY is required for embeddings"
            raise RuntimeError(msg)

        errors: list[str] = []
        skip_primary = nvidia_circuit_open()
        if not skip_primary:
            try:
                return await self._embed_openrouter(
                    texts, self._settings.embeddings_model, "nvidia"
                )
            except Exception as exc:  # noqa: BLE001
                logger.warning(
                    "Primary embedding model failed (%s): %s",
                    self._settings.embeddings_model,
                    exc,
                )
                errors.append(str(exc))
                if is_embedding_rate_limit(exc):
                    trip_nvidia_circuit(
                        _DAILY_QUOTA_CIRCUIT_SECONDS
                        if is_daily_quota(exc)
                        else _NVIDIA_CIRCUIT_SECONDS
                    )

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
        client = self._openrouter

        async def _once() -> EmbeddingBatch:
            response = await client.embeddings.create(
                model=model,
                input=texts,
                dimensions=self._settings.embeddings_dimensions,
            )
            vectors = [_validate_dim(item.embedding) for item in response.data]
            return EmbeddingBatch(vectors=vectors, model=model, provider=provider)

        def _on_retry(next_attempt: int, total: int, exc: BaseException, delay: float) -> None:
            logger.info(
                "%s embed attempt %s/%s after %s; sleeping %.1fs",
                provider.capitalize(),
                next_attempt,
                total,
                type(exc).__name__,
                delay,
            )

        return await call_with_backoff(
            _once,
            is_retryable=is_retryable_embedding_error,
            max_attempts=_EMBED_ATTEMPTS,
            retry_after=lambda exc: retry_after_from_headers(headers_from_exc(exc)),
            delay_floors=_429_FLOORS,
            delay_cap=60.0,
            on_retry=_on_retry,
        )


def _validate_dim(vector: list[float]) -> list[float]:
    settings = get_settings()
    if len(vector) != settings.embeddings_dimensions:
        msg = (
            f"Expected embedding dim {settings.embeddings_dimensions}, got {len(vector)}. "
            "Check EMBEDDINGS_MODEL / EMBEDDINGS_MODEL_FALLBACK on OpenRouter."
        )
        raise ValueError(msg)
    return vector
