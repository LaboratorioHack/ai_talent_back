from unittest.mock import AsyncMock, MagicMock

import pytest
from pydantic import ValidationError

from ai_engine.agents.assistant.agent import AssistantAgent, system_prompt
from ai_engine.schemas.assistant import AssistantRequest


def test_prompt_depends_on_audience_and_carries_context() -> None:
    company = system_prompt("company", {"vacancies": [{"title": "SRE"}]})
    applicant = system_prompt("applicant", {"applications": []})
    assert "equipo de selección" in company
    assert "SRE" in company
    assert "se postuló" in applicant
    assert "otras personas" in applicant
    assert "<context>" in company
    assert "<context>" in applicant


@pytest.mark.asyncio
async def test_reply_sends_the_whole_conversation() -> None:
    llm = MagicMock()
    llm.complete_chat = AsyncMock(return_value="Hola, te ayudo.")
    request = AssistantRequest(
        audience="applicant",
        context={},
        messages=[
            {"role": "user", "content": "Hola"},
            {"role": "assistant", "content": "¿En qué te ayudo?"},
            {"role": "user", "content": "¿Cómo va mi postulación?"},
        ],
    )
    response = await AssistantAgent(llm).reply(request)
    assert response.reply == "Hola, te ayudo."
    _system, messages = llm.complete_chat.await_args.args
    assert [m["role"] for m in messages] == ["user", "assistant", "user"]


def test_conversation_must_end_with_the_user() -> None:
    with pytest.raises(ValidationError):
        AssistantRequest(audience="company", messages=[{"role": "assistant", "content": "Hola"}])
