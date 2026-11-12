import uuid
from unittest.mock import AsyncMock

import pytest

from ai_engine.jev.client import JevScreenResult
from ai_engine.schemas.evaluation import EvaluateRequest
from ai_engine.services.evaluation_service import EvaluationService

_ROLE = uuid.UUID("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
_RESUME = uuid.UUID("11111111-1111-1111-1111-111111111111")
_APP = uuid.UUID("99999999-9999-9999-9999-999999999999")
_REQ_DIS = uuid.UUID("dddddddd-dddd-dddd-dddd-dddddddddddd")
_REQ_MAN = uuid.UUID("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
_EVAL = uuid.UUID("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")


def _request() -> EvaluateRequest:
    return EvaluateRequest(
        application_id=_APP,
        role_profile_id=_ROLE,
        resume_id=_RESUME,
    )


def _req(req_id: uuid.UUID, rtype: str, description: str) -> dict:
    return {
        "id": req_id,
        "type": rtype,
        "category": "SKILL",
        "description": description,
        "condition": None,
        "weight": 1,
    }


def _patch_repos(monkeypatch: pytest.MonkeyPatch, requirements: list[dict]) -> AsyncMock:
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.get_resume",
        AsyncMock(
            return_value={
                "id": _RESUME,
                "candidate_id": uuid.uuid4(),
                "professional_data": {"summary": "Go engineer", "skills": ["go"]},
            }
        ),
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.list_requirements",
        AsyncMock(return_value=requirements),
    )
    insert_eval = AsyncMock(return_value=_EVAL)
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.insert_evaluation",
        insert_eval,
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.insert_requirement_result",
        AsyncMock(return_value=uuid.uuid4()),
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.insert_evidence",
        AsyncMock(return_value=uuid.uuid4()),
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.append_audit_log",
        AsyncMock(),
    )
    return insert_eval


def _claude_service() -> EvaluationService:
    service = EvaluationService()
    service._retriever.retrieve_for_requirement = AsyncMock(return_value=[])  # type: ignore[method-assign]
    service._evaluator.evaluate_requirements = AsyncMock(  # type: ignore[method-assign]
        return_value={
            "assessments": [
                {
                    "requirement_id": str(_REQ_MAN),
                    "result": "MEETS",
                    "score": 90,
                    "confidence": 0.9,
                    "justification": "ok",
                    "fragment_ids": [],
                }
            ],
            "strengths": [],
            "gaps": [],
            "reasoning_summary": "ok",
        }
    )
    service._explainer.refine_summary = AsyncMock(return_value="ok")  # type: ignore[method-assign]
    return service


@pytest.mark.asyncio
async def test_disqualify_skips_claude(monkeypatch: pytest.MonkeyPatch) -> None:
    insert_eval = _patch_repos(monkeypatch, [_req(_REQ_DIS, "DISQUALIFYING", "No work auth")])
    service = _claude_service()
    key = f"req_{_REQ_DIS.hex}"
    service._jev.screen = AsyncMock(  # type: ignore[method-assign]
        return_value=JevScreenResult(
            used=True, model="typesafe/jev-1.14", noul_by_key={key: 0.91}
        )
    )

    result = await service.evaluate(_request())

    assert result.disqualified is True
    assert result.total_score == 0
    assert result.fast_pass.skipped_claude is True
    assert result.prompt_version == "jev-fastpass-v1"
    service._evaluator.evaluate_requirements.assert_not_awaited()
    service._retriever.retrieve_for_requirement.assert_not_awaited()
    insert_eval.assert_awaited_once()
    assert insert_eval.await_args.kwargs["prompt_version"] == "jev-fastpass-v1"


@pytest.mark.asyncio
async def test_mandatory_noul_low_skips_claude(monkeypatch: pytest.MonkeyPatch) -> None:
    _patch_repos(monkeypatch, [_req(_REQ_MAN, "MANDATORY", "Go")])
    service = _claude_service()
    key = f"req_{_REQ_MAN.hex}"
    service._jev.screen = AsyncMock(  # type: ignore[method-assign]
        return_value=JevScreenResult(
            used=True, model="typesafe/jev-1.14", noul_by_key={key: 0.1}
        )
    )

    result = await service.evaluate(_request())

    assert result.fulfillment == "DOES_NOT_MEET"
    assert result.recommend_interview is False
    assert result.fast_pass.skipped_claude is True
    service._evaluator.evaluate_requirements.assert_not_awaited()


@pytest.mark.asyncio
async def test_uncertain_noul_still_calls_claude(monkeypatch: pytest.MonkeyPatch) -> None:
    _patch_repos(monkeypatch, [_req(_REQ_MAN, "MANDATORY", "Go")])
    service = _claude_service()
    key = f"req_{_REQ_MAN.hex}"
    service._jev.screen = AsyncMock(  # type: ignore[method-assign]
        return_value=JevScreenResult(
            used=True, model="typesafe/jev-1.14", noul_by_key={key: 0.5}
        )
    )

    result = await service.evaluate(_request())

    assert result.fast_pass.used is True
    assert result.fast_pass.skipped_claude is False
    service._evaluator.evaluate_requirements.assert_awaited_once()
    service._retriever.retrieve_for_requirement.assert_awaited()
    assert result.confidence == 0.0


@pytest.mark.asyncio
async def test_jev_exception_still_calls_claude(monkeypatch: pytest.MonkeyPatch) -> None:
    _patch_repos(monkeypatch, [_req(_REQ_MAN, "MANDATORY", "Go")])
    service = _claude_service()
    service._jev.screen = AsyncMock(side_effect=RuntimeError("jev down"))  # type: ignore[method-assign]

    result = await service.evaluate(_request())

    assert result.fast_pass.used is False
    assert result.fast_pass.skipped_claude is False
    service._evaluator.evaluate_requirements.assert_awaited_once()
    assert result.reasoning_summary == "ok"
