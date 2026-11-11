from fastapi import APIRouter, HTTPException

from ai_engine.rag.retrieval.evidence_retriever import EvidenceRetriever
from ai_engine.schemas.evaluation import (
    EvidenceChunk,
    EvidenceSearchRequest,
    EvidenceSearchResponse,
)

router = APIRouter(prefix="/v1/rag", tags=["rag"])


@router.post("/evidence", response_model=EvidenceSearchResponse)
async def search_evidence(body: EvidenceSearchRequest) -> EvidenceSearchResponse:
    try:
        rows = await EvidenceRetriever().search(body.resume_id, body.query, limit=body.limit)
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc

    chunks = [
        EvidenceChunk(
            fragment_id=row.fragment_id,
            section=row.section,
            citation=row.citation,
            page_number=row.page_number,
            similarity=row.similarity,
        )
        for row in rows
    ]
    return EvidenceSearchResponse(resume_id=body.resume_id, query=body.query, chunks=chunks)
