from fastapi import APIRouter, HTTPException

from ai_engine.agents.assistant.agent import AssistantAgent
from ai_engine.schemas.assistant import AssistantRequest, AssistantResponse

router = APIRouter(prefix="/v1/assistant", tags=["assistant"])


@router.post("/chat", response_model=AssistantResponse)
async def chat(body: AssistantRequest) -> AssistantResponse:
    try:
        return await AssistantAgent().reply(body)
    except RuntimeError as exc:
        raise HTTPException(status_code=502, detail=str(exc)) from exc
