import asyncio
import uuid
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest

from ai_engine.rag.embeddings.client import EmbeddingBatch
from ai_engine.schemas.cv import StructuredCandidateProfile
from ai_engine.services.ingest_service import IngestService


@pytest.mark.asyncio
async def test_persist_text_runs_extract_and_embed_in_parallel(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    extract_started = asyncio.Event()
    release_extract = asyncio.Event()
    embed_saw_extract = {"value": False}

    async def slow_extract(
        _text: str,
        _llm: object | None = None,
        sanitization: object | None = None,
    ) -> StructuredCandidateProfile:
        extract_started.set()
        await release_extract.wait()
        return StructuredCandidateProfile(summary="ok")

    async def embed(texts: list[str]) -> EmbeddingBatch:
        embed_saw_extract["value"] = extract_started.is_set()
        release_extract.set()
        dim = [0.01] * 1024
        return EmbeddingBatch([dim for _ in texts], "nvidia/nemotron-3.5-embed-1b:free", "nvidia")

    monkeypatch.setattr("ai_engine.services.ingest_service.extract_profile", slow_extract)
    monkeypatch.setattr(
        "ai_engine.services.ingest_service.anonymize_cv_text_with_report",
        lambda text: (text, None),
    )
    monkeypatch.setattr(
        "ai_engine.services.ingest_service.chunk_cv_text",
        lambda _text, pages=None: [
            SimpleNamespace(position=0, section="s", source_location={}, content="hello")
        ],
    )
    persist = AsyncMock(return_value=[uuid.uuid4()])
    monkeypatch.setattr(
        "ai_engine.services.ingest_service.repositories.persist_ingested_resume",
        persist,
    )

    service = IngestService(llm=MagicMock(), embeddings=MagicMock())
    service._embeddings.embed_texts = embed  # type: ignore[method-assign]
    result = await service._persist_text(
        uuid.uuid4(),
        "cv text",
        parser_strategy="csv_text",
        ocr_confidence=None,
        page_tuples=None,
    )
    assert embed_saw_extract["value"] is True
    assert result.chunks_count == 1
    persist.assert_awaited_once()
