from fastapi import APIRouter, HTTPException

from ai_engine.schemas.evaluation import (
    EvaluateRequest,
    EvaluateResponse,
    RankCandidatesRequest,
    RankCandidatesResponse,
)
from ai_engine.services.evaluation_service import EvaluationService

router = APIRouter(prefix="/v1/evaluations", tags=["evaluations"])


@router.post("/evaluate", response_model=EvaluateResponse)
async def evaluate_candidate(body: EvaluateRequest) -> EvaluateResponse:
    try:
        return await EvaluationService().evaluate(body)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@router.post("/rank", response_model=RankCandidatesResponse)
async def rank_candidates(body: RankCandidatesRequest) -> RankCandidatesResponse:
    try:
        return await EvaluationService().rank(body)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
