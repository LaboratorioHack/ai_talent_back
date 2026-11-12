import uuid
from unittest.mock import AsyncMock

import pytest
from pydantic import ValidationError

from ai_engine.schemas.evaluation import (
    CompareCandidatesRequest,
    ComparerResult,
    EvaluateResponse,
    SideBySideCandidate,
)
from ai_engine.services.evaluation_service import EvaluationService

_ROLE = uuid.UUID("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
_R1 = uuid.UUID("11111111-1111-1111-1111-111111111111")
_R2 = uuid.UUID("22222222-2222-2222-2222-222222222222")
_R3 = uuid.UUID("33333333-3333-3333-3333-333333333333")
_R4 = uuid.UUID("44444444-4444-4444-4444-444444444444")
_EVAL = uuid.UUID("55555555-5555-5555-5555-555555555555")
_CANDIDATE = uuid.UUID("66666666-6666-6666-6666-666666666666")
_REQ = uuid.UUID("77777777-7777-7777-7777-777777777777")


def _resume(resume_id: uuid.UUID) -> dict:
    return {
        "id": resume_id,
        "candidate_id": _CANDIDATE,
        "professional_data": {
            "summary": "Backend",
            "skills": ["go"],
            "bias_guardrail_telemetry": {"pii_scrubbed": True},
            "parser_strategy": "native_layout",
            "ocr_confidence": 0.9,
            "extractor_version": "v1.1.0",
        },
    }


def _stored_eval() -> dict:
    return {
        "id": _EVAL,
        "total_score": 80,
        "fulfillment": "MEETS",
        "confidence": 0.8,
        "disqualified": False,
        "recommend_interview": True,
        "strengths": [],
        "gaps": [],
    }


def _compare_result() -> ComparerResult:
    return ComparerResult(
        comparative_summary="Two strong backends.",
        side_by_side_matrix=[
            SideBySideCandidate(
                candidate_id=str(_R1),
                primary_advantage="Deeper Go",
                primary_trade_off_or_gap="Less Kubernetes",
                ideal_team_fit="Choose for backend depth",
            )
        ],
        head_to_head_trade_off="A vs B",
        recommended_interview_focus=[],
    )


def _patch_common(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.get_role_profile",
        AsyncMock(
            return_value={
                "id": _ROLE,
                "job_opening_id": uuid.uuid4(),
                "description": "Staff Go",
            }
        ),
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.list_requirements",
        AsyncMock(
            return_value=[
                {
                    "id": _REQ,
                    "type": "MANDATORY",
                    "category": "SKILL",
                    "description": "Go",
                    "condition": None,
                    "weight": 1,
                }
            ]
        ),
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.get_resume",
        AsyncMock(side_effect=_resume),
    )
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.list_requirement_results",
        AsyncMock(
            return_value=[
                {
                    "requirement_id": _REQ,
                    "type": "MANDATORY",
                    "description": "Go",
                    "result": "MEETS",
                    "score": 90,
                    "justification": "5 years Go",
                }
            ]
        ),
    )


@pytest.mark.asyncio
async def test_compare_reuses_latest_evaluation(monkeypatch: pytest.MonkeyPatch) -> None:
    _patch_common(monkeypatch)
    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.get_latest_evaluation",
        AsyncMock(return_value=_stored_eval()),
    )
    service = EvaluationService()
    service.evaluate = AsyncMock()  # type: ignore[method-assign]
    service._comparer.compare = AsyncMock(return_value=_compare_result())  # type: ignore[method-assign]

    result = await service.compare(
        CompareCandidatesRequest(role_profile_id=_ROLE, resume_ids=[_R1, _R2])
    )

    assert result.role_profile_id == _ROLE
    assert result.comparative_summary == "Two strong backends."
    service.evaluate.assert_not_awaited()
    job, candidates = service._comparer.compare.await_args.args
    assert job["description"] == "Staff Go"
    assert len(candidates) == 2
    for bundle in candidates:
        assert bundle["professional_data"]["summary"] == "Backend"
        assert "bias_guardrail_telemetry" not in bundle["professional_data"]
        assert "parser_strategy" not in bundle["professional_data"]
        assert "ocr_confidence" not in bundle["professional_data"]
        assert "extractor_version" not in bundle["professional_data"]


@pytest.mark.asyncio
async def test_compare_evaluates_missing_resume(monkeypatch: pytest.MonkeyPatch) -> None:
    _patch_common(monkeypatch)

    async def latest(_role: uuid.UUID, resume_id: uuid.UUID) -> dict | None:
        if resume_id == _R1:
            return _stored_eval()
        return None

    monkeypatch.setattr(
        "ai_engine.services.evaluation_service.repositories.get_latest_evaluation",
        latest,
    )
    evaluated = EvaluateResponse(
        evaluation_id=uuid.uuid4(),
        total_score=70,
        fulfillment="PARTIALLY_MEETS",
        confidence=0.7,
        recommend_interview=True,
        disqualified=False,
        strengths=[],
        gaps=[],
        requirement_assessments=[],
        ai_model="claude",
        prompt_version="eval-v1",
        reasoning_summary="ok",
    )
    service = EvaluationService()
    service.evaluate = AsyncMock(return_value=evaluated)  # type: ignore[method-assign]
    service._comparer.compare = AsyncMock(return_value=_compare_result())  # type: ignore[method-assign]

    await service.compare(CompareCandidatesRequest(role_profile_id=_ROLE, resume_ids=[_R1, _R2]))

    service.evaluate.assert_awaited_once()
    assert service.evaluate.await_args.args[0].resume_id == _R2
    candidates = service._comparer.compare.await_args.args[1]
    assert {c["resume_id"] for c in candidates} == {str(_R1), str(_R2)}


@pytest.mark.asyncio
async def test_compare_rejects_wrong_count() -> None:
    service = EvaluationService()
    with pytest.raises(ValueError, match="2 or 3"):
        await service.compare(
            CompareCandidatesRequest.model_construct(role_profile_id=_ROLE, resume_ids=[_R1])
        )
    with pytest.raises(ValueError, match="2 or 3"):
        await service.compare(
            CompareCandidatesRequest.model_construct(
                role_profile_id=_ROLE, resume_ids=[_R1, _R2, _R3, _R4]
            )
        )


def test_compare_request_rejects_duplicates() -> None:
    with pytest.raises(ValidationError):
        CompareCandidatesRequest(role_profile_id=_ROLE, resume_ids=[_R1, _R1])
