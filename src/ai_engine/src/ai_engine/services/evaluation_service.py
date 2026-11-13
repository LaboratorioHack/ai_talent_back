"""Evaluate and rank candidates against role profiles."""

from __future__ import annotations

import logging
import uuid
from typing import Any

from ai_engine.agents.comparer.agent import ComparerAgent
from ai_engine.agents.evaluator.agent import EvaluatorAgent
from ai_engine.agents.explainer.agent import ExplainerAgent
from ai_engine.core.config import get_settings
from ai_engine.jev.client import (
    JevClient,
    JevNoulQuestion,
    JevScreenResult,
    blend_confidence,
    hard_fail_keys,
)
from ai_engine.persistence import repositories
from ai_engine.rag.retrieval.evidence_retriever import EvidenceRetriever
from ai_engine.schemas.evaluation import (
    CompareCandidatesRequest,
    CompareCandidatesResponse,
    EvaluateRequest,
    EvaluateResponse,
    EvidenceChunk,
    FastPassInfo,
    RankCandidatesRequest,
    RankCandidatesResponse,
    RankedCandidate,
    RequirementAssessment,
)
from ai_engine.scoring.calculator import RequirementScoreInput, compute_scores
from ai_engine.services.assessment_parsing import as_points, parse_assessments

logger = logging.getLogger(__name__)

_FASTPASS_PROMPT = "jev-fastpass-v1"

_PROFILE_DROP_KEYS = frozenset(
    {"bias_guardrail_telemetry", "parser_strategy", "ocr_confidence", "extractor_version"}
)


class EvaluationService:
    def __init__(self) -> None:
        self._retriever = EvidenceRetriever()
        self._evaluator = EvaluatorAgent()
        self._explainer = ExplainerAgent()
        self._comparer = ComparerAgent()
        self._jev = JevClient()

    async def evaluate(self, request: EvaluateRequest) -> EvaluateResponse:
        resume = await repositories.get_resume(request.resume_id)
        if resume is None:
            raise ValueError("Resume not found")

        application_id = request.application_id
        if application_id is None:
            if not request.job_opening_id or not request.candidate_id:
                raise ValueError("application_id or (job_opening_id + candidate_id) required")
            application_id = await repositories.get_or_create_application(
                request.job_opening_id,
                request.candidate_id,
            )

        requirements = await repositories.list_requirements(request.role_profile_id)
        profile = resume.get("professional_data") or {}
        questions = _jev_questions(requirements)
        screen = await self._screen(profile, questions)
        failed = hard_fail_keys(
            questions,
            screen.noul_by_key,
            yes_threshold=get_settings().jev_noul_yes,
            no_threshold=get_settings().jev_noul_no,
        )
        if screen.used and failed:
            return await self._evaluate_fast_pass(
                request,
                application_id=application_id,
                requirements=requirements,
                questions=questions,
                screen=screen,
                failed=failed,
            )

        evidence_by_req: dict[uuid.UUID, list] = {}
        for req in requirements:
            text = f"{req.get('description', '')} {req.get('condition') or ''}".strip()
            evidence_by_req[req["id"]] = await self._retriever.retrieve_for_requirement(
                request.resume_id,
                text,
            )
        llm_result = await self._evaluator.evaluate_requirements(
            requirements, evidence_by_req, profile
        )
        reasoning = llm_result.get("reasoning_summary", "")
        reasoning = await self._explainer.refine_summary(
            {**llm_result, "reasoning_summary": reasoning}
        )

        assessments_raw = llm_result.get("assessments") or []
        req_map = {r["id"]: r for r in requirements}
        score_inputs: list[RequirementScoreInput] = []
        response_assessments: list[RequirementAssessment] = []

        settings = get_settings()
        ocr_conf = (profile or {}).get("ocr_confidence")

        eval_id: uuid.UUID | None = None

        for parsed in parse_assessments(assessments_raw, list(req_map)):
            req_id = parsed.requirement_id
            req_row = req_map[req_id]
            result = parsed.result
            score = parsed.score
            confidence = parsed.confidence
            justification = parsed.justification
            frag_ids = parsed.fragment_ids

            evidences: list[EvidenceChunk] = []
            for chunk in evidence_by_req.get(req_id, []):
                if chunk.fragment_id in frag_ids or not frag_ids:
                    evidences.append(
                        EvidenceChunk(
                            fragment_id=chunk.fragment_id,
                            section=chunk.section,
                            citation=chunk.citation,
                            page_number=chunk.page_number,
                            similarity=chunk.similarity,
                        )
                    )

            score_inputs.append(
                RequirementScoreInput(
                    type=req_row["type"],
                    weight=float(req_row.get("weight") or 1),
                    result=result,
                    score=score,
                    confidence=confidence,
                )
            )
            response_assessments.append(
                RequirementAssessment(
                    requirement_id=req_id,
                    result=result,
                    score=score,
                    confidence=confidence,
                    justification=justification,
                    evidence=evidences[:5],
                )
            )

        scored = compute_scores(score_inputs, ocr_confidence=ocr_conf)
        confidence = blend_confidence(scored.confidence, screen.certainty if screen.used else None)
        fast_pass = FastPassInfo(
            used=screen.used,
            skipped_claude=False,
            model=screen.model,
            certainty=screen.certainty if screen.used else None,
        )

        eval_id = await repositories.insert_evaluation(
            application_id=application_id,
            role_profile_id=request.role_profile_id,
            resume_id=request.resume_id,
            total_score=scored.total_score,
            fulfillment=scored.fulfillment,
            disqualified=scored.disqualified,
            confidence=confidence,
            strengths=as_points(llm_result.get("strengths")),
            gaps=as_points(llm_result.get("gaps")),
            recommend_interview=scored.recommend_interview,
            ai_model=self._evaluator.ai_model,
            prompt_version=self._evaluator.prompt_version,
        )

        for _item, resp in zip(assessments_raw, response_assessments, strict=False):
            req_id = resp.requirement_id
            rr_id = await repositories.insert_requirement_result(
                evaluation_id=eval_id,
                requirement_id=req_id,
                result=resp.result,
                score=resp.score,
                confidence=resp.confidence,
                justification=resp.justification,
            )
            for ev in resp.evidence:
                await repositories.insert_evidence(
                    requirement_result_id=rr_id,
                    resume_fragment_id=ev.fragment_id,
                    citation=ev.citation[:1000],
                )

        await repositories.append_audit_log(
            action="evaluation.completed",
            entity="evaluation",
            entity_id=str(eval_id),
            details={"total_score": scored.total_score, "fulfillment": scored.fulfillment},
        )

        return EvaluateResponse(
            evaluation_id=eval_id,
            total_score=scored.total_score,
            fulfillment=scored.fulfillment,
            confidence=confidence,
            recommend_interview=scored.recommend_interview,
            disqualified=scored.disqualified,
            strengths=as_points(llm_result.get("strengths")),
            gaps=as_points(llm_result.get("gaps")),
            requirement_assessments=response_assessments,
            ai_model=self._evaluator.ai_model,
            prompt_version=settings.prompt_version,
            reasoning_summary=reasoning,
            fast_pass=fast_pass,
        )

    async def _screen(
        self, profile: dict[str, Any], questions: list[JevNoulQuestion]
    ) -> JevScreenResult:
        if not questions:
            return JevScreenResult(used=False)
        try:
            state = _anonymized_profile({"professional_data": profile})
            return await self._jev.screen(state, questions)
        except Exception as exc:  # noqa: BLE001
            logger.warning("Jev screen failed; continuing with Claude: %s", exc)
            return JevScreenResult(used=False)

    async def _evaluate_fast_pass(
        self,
        request: EvaluateRequest,
        *,
        application_id: uuid.UUID,
        requirements: list[dict[str, Any]],
        questions: list[JevNoulQuestion],
        screen: JevScreenResult,
        failed: set[str],
    ) -> EvaluateResponse:
        settings = get_settings()
        type_by_key = {q.key: q.req_type for q in questions}
        score_inputs: list[RequirementScoreInput] = []
        response_assessments: list[RequirementAssessment] = []
        gaps: list[dict[str, Any]] = []

        for req in requirements:
            req_id = uuid.UUID(str(req["id"]))
            key = f"req_{req_id.hex}"
            noul = screen.noul_by_key.get(key)
            rtype = str(req.get("type") or "")
            if key in failed:
                if type_by_key.get(key) == "DISQUALIFYING":
                    result, score = "MEETS", 100.0
                    conf = float(noul or 1.0)
                else:
                    result, score = "DOES_NOT_MEET", 0.0
                    conf = 1.0 - float(noul or 0.0)
                noul_label = f"{noul:.2f}" if noul is not None else "n/a"
                justification = f"Jev fast-pass noul={noul_label} (skipped Claude)"
                gaps.append(
                    {
                        "requirement_id": str(req_id),
                        "type": rtype,
                        "description": req.get("description"),
                    }
                )
            else:
                result, score, conf = "NO_EVIDENCE", 0.0, 0.5
                justification = "Jev fast-pass did not flag this requirement; Claude skipped."

            score_inputs.append(
                RequirementScoreInput(
                    type=rtype,
                    weight=float(req.get("weight") or 1),
                    result=result,
                    score=score,
                    confidence=conf,
                )
            )
            response_assessments.append(
                RequirementAssessment(
                    requirement_id=req_id,
                    result=result,
                    score=score,
                    confidence=conf,
                    justification=justification,
                )
            )

        scored = compute_scores(score_inputs, ocr_confidence=None)
        confidence = screen.certainty if screen.certainty is not None else scored.confidence
        ai_model = screen.model or settings.jev_model_id
        eval_id = await repositories.insert_evaluation(
            application_id=application_id,
            role_profile_id=request.role_profile_id,
            resume_id=request.resume_id,
            total_score=scored.total_score,
            fulfillment=scored.fulfillment,
            disqualified=scored.disqualified,
            confidence=confidence,
            strengths=[],
            gaps=gaps,
            recommend_interview=scored.recommend_interview,
            ai_model=ai_model,
            prompt_version=_FASTPASS_PROMPT,
        )
        for resp in response_assessments:
            await repositories.insert_requirement_result(
                evaluation_id=eval_id,
                requirement_id=resp.requirement_id,
                result=resp.result,
                score=resp.score,
                confidence=resp.confidence,
                justification=resp.justification,
            )
        await repositories.append_audit_log(
            action="evaluation.completed",
            entity="evaluation",
            entity_id=str(eval_id),
            details={
                "total_score": scored.total_score,
                "fulfillment": scored.fulfillment,
                "fast_pass": True,
            },
        )
        return EvaluateResponse(
            evaluation_id=eval_id,
            total_score=scored.total_score,
            fulfillment=scored.fulfillment,
            confidence=confidence,
            recommend_interview=scored.recommend_interview,
            disqualified=scored.disqualified,
            strengths=[],
            gaps=gaps,
            requirement_assessments=response_assessments,
            ai_model=ai_model,
            prompt_version=_FASTPASS_PROMPT,
            reasoning_summary="Auto-filtered by Jev; Claude skipped.",
            fast_pass=FastPassInfo(
                used=True,
                skipped_claude=True,
                model=ai_model,
                certainty=screen.certainty,
            ),
        )

    async def rank(self, request: RankCandidatesRequest) -> RankCandidatesResponse:
        ranked: list[RankedCandidate] = []
        for resume_id in request.resume_ids:
            resume = await repositories.get_resume(resume_id)
            if resume is None:
                continue
            eval_req = EvaluateRequest(
                role_profile_id=request.role_profile_id,
                resume_id=resume_id,
                candidate_id=resume["candidate_id"],
                job_opening_id=None,
            )
            profile = await repositories.get_role_profile(request.role_profile_id)
            if profile:
                eval_req.job_opening_id = profile["job_opening_id"]
            try:
                result = await self.evaluate(eval_req)
            except (ValueError, RuntimeError):
                continue
            ranked.append(
                RankedCandidate(
                    resume_id=resume_id,
                    evaluation_id=result.evaluation_id,
                    total_score=result.total_score,
                    confidence=result.confidence,
                    fulfillment=result.fulfillment,
                    recommend_interview=result.recommend_interview,
                )
            )
        ranked.sort(key=lambda r: (r.total_score, r.confidence), reverse=True)
        return RankCandidatesResponse(role_profile_id=request.role_profile_id, ranked=ranked)

    async def compare(self, request: CompareCandidatesRequest) -> CompareCandidatesResponse:
        resume_ids = list(request.resume_ids)
        if len(resume_ids) < 2 or len(resume_ids) > 3:
            raise ValueError("compare requires 2 or 3 resume_ids")
        if len(set(resume_ids)) != len(resume_ids):
            raise ValueError("resume_ids must be unique")

        profile = await repositories.get_role_profile(request.role_profile_id)
        if profile is None:
            raise ValueError("Role profile not found")
        requirements = await repositories.list_requirements(request.role_profile_id)
        req_lookup = {r["id"]: r for r in requirements}

        job_vacancy = {
            "description": profile.get("description") or "",
            "requirements": [
                {
                    "id": str(r["id"]),
                    "type": r.get("type"),
                    "category": r.get("category"),
                    "description": r.get("description"),
                    "condition": r.get("condition"),
                    "weight": r.get("weight"),
                }
                for r in requirements
            ],
        }

        candidates: list[dict[str, Any]] = []
        for resume_id in resume_ids:
            resume = await repositories.get_resume(resume_id)
            if resume is None:
                raise ValueError(f"Resume {resume_id} not found")

            latest = await repositories.get_latest_evaluation(request.role_profile_id, resume_id)
            if latest is None:
                evaluated = await self.evaluate(
                    EvaluateRequest(
                        role_profile_id=request.role_profile_id,
                        resume_id=resume_id,
                        candidate_id=resume["candidate_id"],
                        job_opening_id=profile.get("job_opening_id"),
                    )
                )
                candidates.append(_bundle_from_evaluate(resume_id, resume, evaluated, req_lookup))
            else:
                results = await repositories.list_requirement_results(latest["id"])
                candidates.append(_bundle_from_stored(resume_id, resume, latest, results))

        result = await self._comparer.compare(job_vacancy, candidates)
        return CompareCandidatesResponse(
            role_profile_id=request.role_profile_id,
            comparative_summary=result.comparative_summary,
            side_by_side_matrix=result.side_by_side_matrix,
            head_to_head_trade_off=result.head_to_head_trade_off,
            recommended_interview_focus=result.recommended_interview_focus,
        )


def _anonymized_profile(resume: dict[str, Any]) -> dict[str, Any]:
    data = dict(resume.get("professional_data") or {})
    for key in _PROFILE_DROP_KEYS:
        data.pop(key, None)
    return data


def _jev_questions(requirements: list[dict[str, Any]]) -> list[JevNoulQuestion]:
    questions: list[JevNoulQuestion] = []
    for req in requirements:
        rtype = str(req.get("type") or "")
        if rtype not in {"MANDATORY", "DISQUALIFYING"}:
            continue
        req_id = uuid.UUID(str(req["id"]))
        text = f"{req.get('description', '')} {req.get('condition') or ''}".strip()
        if not text:
            continue
        if rtype == "DISQUALIFYING":
            instructions = (
                f"Does this candidate clearly exhibit this disqualifying condition: {text}?"
            )
        else:
            instructions = f"Does this candidate meet: {text}?"
        questions.append(
            JevNoulQuestion(key=f"req_{req_id.hex}", req_type=rtype, instructions=instructions)
        )
    return questions


def _bundle_from_evaluate(
    resume_id: uuid.UUID,
    resume: dict[str, Any],
    evaluated: EvaluateResponse,
    req_lookup: dict[uuid.UUID, dict[str, Any]],
) -> dict[str, Any]:
    return {
        "candidate_id": str(resume_id),
        "resume_id": str(resume_id),
        "total_score": evaluated.total_score,
        "fulfillment": evaluated.fulfillment,
        "confidence": evaluated.confidence,
        "disqualified": evaluated.disqualified,
        "recommend_interview": evaluated.recommend_interview,
        "strengths": evaluated.strengths,
        "gaps": evaluated.gaps,
        "requirement_results": [
            {
                "requirement_id": str(item.requirement_id),
                "type": (req_lookup.get(item.requirement_id) or {}).get("type"),
                "description": (req_lookup.get(item.requirement_id) or {}).get("description"),
                "result": item.result,
                "score": item.score,
                "justification": item.justification,
            }
            for item in evaluated.requirement_assessments
        ],
        "professional_data": _anonymized_profile(resume),
    }


def _bundle_from_stored(
    resume_id: uuid.UUID,
    resume: dict[str, Any],
    evaluation: dict[str, Any],
    results: list[dict[str, Any]],
) -> dict[str, Any]:
    return {
        "candidate_id": str(resume_id),
        "resume_id": str(resume_id),
        "total_score": float(evaluation.get("total_score") or 0),
        "fulfillment": evaluation.get("fulfillment"),
        "confidence": float(evaluation.get("confidence") or 0),
        "disqualified": bool(evaluation.get("disqualified")),
        "recommend_interview": bool(evaluation.get("recommend_interview")),
        "strengths": evaluation.get("strengths") or [],
        "gaps": evaluation.get("gaps") or [],
        "requirement_results": [
            {
                "requirement_id": str(row["requirement_id"]),
                "type": row.get("type"),
                "description": row.get("description"),
                "result": row.get("result"),
                "score": float(row.get("score") or 0),
                "justification": row.get("justification") or "",
            }
            for row in results
        ],
        "professional_data": _anonymized_profile(resume),
    }
