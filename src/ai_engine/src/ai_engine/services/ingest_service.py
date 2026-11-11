"""End-to-end CV ingest aligned to app.resumes / resume_fragments."""

from __future__ import annotations

import hashlib
import uuid
from typing import BinaryIO

from ai_engine.anonymization.sanitizer import anonymize_cv_text
from ai_engine.core.config import get_settings
from ai_engine.core.llm import LlmClient
from ai_engine.extraction.extractor import extract_profile
from ai_engine.ingestion.parsers.docx_parser import parse_docx
from ai_engine.ingestion.parsers.pdf_parser import PdfParser
from ai_engine.ingestion.validation import CvFormat, InvalidCvError, detect_cv_format
from ai_engine.persistence import repositories
from ai_engine.persistence.storage import download_cv
from ai_engine.rag.chunker import chunk_cv_text
from ai_engine.rag.embeddings.client import EmbeddingsClient
from ai_engine.schemas.cv import CvIngestResponse


class IngestService:
    def __init__(
        self,
        llm: LlmClient | None = None,
        embeddings: EmbeddingsClient | None = None,
    ) -> None:
        self._llm = llm or LlmClient()
        self._embeddings = embeddings or EmbeddingsClient()
        self._pdf = PdfParser(self._llm)

    async def ingest_resume_bytes(self, resume_id: uuid.UUID, content: bytes) -> CvIngestResponse:
        settings = get_settings()
        await repositories.update_resume_processing(resume_id, status="PROCESSING")

        try:
            fmt = detect_cv_format(content, settings.max_upload_bytes)
            parser_strategy = "docx_native"
            ocr_confidence: float | None = None
            page_tuples: list[tuple[int, str]] = []
            raw_text = ""

            if fmt is CvFormat.PDF:
                parsed = await self._pdf.parse(content)
                raw_text = parsed.full_text
                parser_strategy = parsed.parser_strategy
                ocr_confidence = parsed.mean_ocr_confidence
                page_tuples = [(p.page_number, p.text) for p in parsed.pages]
            else:
                raw_text = parse_docx(content).full_text

            anonymized = anonymize_cv_text(raw_text)
            profile = await extract_profile(anonymized, self._llm)
            profile.parser_strategy = parser_strategy
            profile.ocr_confidence = ocr_confidence
            profile.extractor_version = settings.extractor_version

            chunks = chunk_cv_text(anonymized, pages=page_tuples or None)
            texts = [c.content for c in chunks]
            batch = await self._embeddings.embed_texts(texts) if texts else None

            await repositories.delete_resume_fragments(resume_id)
            frag_rows: list[dict] = []
            for idx, chunk in enumerate(chunks):
                vector = batch.vectors[idx] if batch and idx < len(batch.vectors) else None
                frag_rows.append(
                    {
                        "position": chunk.position,
                        "section": chunk.section,
                        "source_location": chunk.source_location,
                        "anonymized_content": chunk.content,
                        "embedding": vector,
                        "embedding_model": batch.model if vector else None,
                    }
                )
            await repositories.insert_resume_fragments(resume_id, frag_rows)

            professional = profile.model_dump()
            await repositories.update_resume_processing(
                resume_id,
                status="PROCESSED",
                professional_data=professional,
                extractor_version=settings.extractor_version,
            )
            await repositories.append_audit_log(
                action="resume.processed",
                entity="resume",
                entity_id=str(resume_id),
                details={"chunks": len(frag_rows), "parser_strategy": parser_strategy},
            )
            return CvIngestResponse(
                resume_id=resume_id,
                extractor_version=settings.extractor_version,
                parser_strategy=parser_strategy,
                ocr_confidence=ocr_confidence,
                chunks_count=len(frag_rows),
                professional_data=profile,
            )
        except (InvalidCvError, ValueError, RuntimeError) as exc:
            await repositories.update_resume_processing(
                resume_id,
                status="FAILED",
                processing_error=str(exc)[:2000],
            )
            raise

    async def ingest_from_storage(self, resume_id: uuid.UUID) -> CvIngestResponse:
        row = await repositories.get_resume(resume_id)
        if row is None:
            msg = f"Resume {resume_id} not found"
            raise ValueError(msg)
        content = download_cv(row["file_key"])
        return await self.ingest_resume_bytes(resume_id, content)

    async def ingest_upload(self, resume_id: uuid.UUID, file: BinaryIO) -> CvIngestResponse:
        content = file.read()
        file_hash = hashlib.sha256(content).hexdigest()
        _ = file_hash  # resume.file_hash may be set by API on upload
        return await self.ingest_resume_bytes(resume_id, content)
