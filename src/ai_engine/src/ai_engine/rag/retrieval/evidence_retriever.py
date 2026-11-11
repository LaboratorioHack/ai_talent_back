from __future__ import annotations

import uuid
from dataclasses import dataclass

from ai_engine.persistence import repositories
from ai_engine.rag.embeddings.client import EmbeddingsClient
from ai_engine.rag.knowledge.taxonomy import expand_query_terms


@dataclass
class RetrievedEvidence:
    fragment_id: uuid.UUID
    section: str | None
    citation: str
    page_number: int | None
    similarity: float
    embedding_model: str | None


class EvidenceRetriever:
    def __init__(self, embeddings: EmbeddingsClient | None = None) -> None:
        self._embeddings = embeddings or EmbeddingsClient()

    async def retrieve_for_requirement(
        self,
        resume_id: uuid.UUID,
        requirement_text: str,
        *,
        limit: int = 6,
    ) -> list[RetrievedEvidence]:
        query = expand_query_terms(requirement_text)
        batch = await self._embeddings.embed_texts([query])
        if not batch.vectors:
            return []
        rows = await repositories.search_fragments_by_embedding(
            resume_id,
            batch.vectors[0],
            limit=limit,
        )
        results: list[RetrievedEvidence] = []
        for row in rows:
            loc = row.get("source_location") or {}
            page = loc.get("page_number")
            results.append(
                RetrievedEvidence(
                    fragment_id=row["id"],
                    section=row.get("section"),
                    citation=(row.get("anonymized_content") or "")[:500],
                    page_number=page,
                    similarity=float(row.get("similarity") or 0),
                    embedding_model=row.get("embedding_model"),
                )
            )
        return results

    async def search(
        self, resume_id: uuid.UUID, query: str, *, limit: int = 8
    ) -> list[RetrievedEvidence]:
        return await self.retrieve_for_requirement(resume_id, query, limit=limit)
