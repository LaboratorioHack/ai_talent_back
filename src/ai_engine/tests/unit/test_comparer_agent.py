from unittest.mock import AsyncMock, MagicMock

import pytest

from ai_engine.agents.comparer.agent import ComparerAgent
from ai_engine.schemas.evaluation import ComparerResult

_RESUME_ID = "11111111-1111-1111-1111-111111111111"


@pytest.mark.asyncio
async def test_comparer_sends_job_spec_and_resume_ids() -> None:
    llm = MagicMock()
    llm.complete_json = AsyncMock(
        return_value=ComparerResult(
            comparative_summary="Pool is strong on backend depth.",
            head_to_head_trade_off="A has deeper Go; B has Kubernetes.",
        )
    )
    agent = ComparerAgent(llm=llm)
    job = {
        "description": "Staff backend engineer",
        "requirements": [{"type": "MANDATORY", "description": "Go"}],
    }
    candidates = [
        {
            "candidate_id": _RESUME_ID,
            "resume_id": _RESUME_ID,
            "professional_data": {"summary": "Go services"},
        }
    ]

    result = await agent.compare(job, candidates)

    assert result.comparative_summary.startswith("Pool is strong")
    llm.complete_json.assert_awaited_once()
    system, user, model_type = llm.complete_json.await_args.args
    kwargs = llm.complete_json.await_args.kwargs
    assert "protected attributes" in system
    assert "Staff backend engineer" in user
    assert _RESUME_ID in user
    assert model_type is ComparerResult
    assert kwargs["tool_name"] == "compare_candidates"
    assert kwargs["max_tokens"] == 4096
