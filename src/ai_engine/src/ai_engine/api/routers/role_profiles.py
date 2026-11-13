from fastapi import APIRouter, HTTPException

from ai_engine.schemas.role_profile import (
    ProposeRequirementsRequest,
    ProposeRequirementsResponse,
    SuggestEquivalencesRequest,
    SuggestEquivalencesResponse,
)
from ai_engine.services.role_profile_service import RoleProfileService

router = APIRouter(prefix="/v1/role-profiles", tags=["role-profiles"])


@router.post("/propose", response_model=ProposeRequirementsResponse)
async def propose_requirements(body: ProposeRequirementsRequest) -> ProposeRequirementsResponse:
    try:
        return await RoleProfileService().propose(body)
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@router.post("/equivalences", response_model=SuggestEquivalencesResponse)
async def suggest_equivalences(body: SuggestEquivalencesRequest) -> SuggestEquivalencesResponse:
    try:
        return await RoleProfileService().suggest_equivalences(body)
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc
