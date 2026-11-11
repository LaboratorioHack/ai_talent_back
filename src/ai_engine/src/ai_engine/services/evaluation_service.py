"""Evaluate and rank candidates against role profiles."""

from __future__ import annotations

import uuid

from ai_engine.agents.evaluator.agent import EvaluatorAgent
from ai_engine.agents.explainer.agent import ExplainerAgent
from ai_engine.core.config import get_settings
from ai_engine.persistence import repositories
from ai_engine.rag.retrieval.evidence_retriever import EvidenceRetriever
from ai_engine.schemas.evaluation import (
    EvaluateRequest,
    EvaluateResponse,
    EvidenceChunk,
    RankCandidatesRequest,
    RankCandidatesResponse,
    RankedCandidate,
    RequirementAssessment,
)
from ai_engine.scoring.calculator import RequirementScoreInput, compute_scores


class EvaluationService:
    def __init__(self) -> None:
        self._retriever = EvidenceRetriever()
        self._evaluator = EvaluatorAgent()
        self._explainer = ExplainerAgent()

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
        evidence_by_req: dict[uuid.UUID, list] = {}
        query_parts: list[str] = []
        for req in requirements:
            text = f"{req.get('description', '')} {req.get('condition') or ''}".strip()
            query_parts.append(text)
            evidence_by_req[req["id"]] = await self._retriever.retrieve_for_requirement(
                request.resume_id,
                text,
            )

        profile = resume.get("professional_data") or {}
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

        for item in assessments_raw:
            req_id = uuid.UUID(str(item.get("requirement_id")))
            req_row = req_map.get(req_id)
            if not req_row:
                continue
            result = str(item.get("result", "NO_EVIDENCE")).upper()
            score = float(item.get("score", 0))
            confidence = float(item.get("confidence", 0.5))
            justification = str(item.get("justification", ""))
            frag_ids = [uuid.UUID(str(x)) for x in item.get("fragment_ids") or []]

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

        eval_id = await repositories.insert_evaluation(
            application_id=application_id,
            role_profile_id=request.role_profile_id,
            resume_id=request.resume_id,
            total_score=scored.total_score,
            fulfillment=scored.fulfillment,
            disqualified=scored.disqualified,
            confidence=scored.confidence,
            strengths=llm_result.get("strengths") or [],
            gaps=llm_result.get("gaps") or [],
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
            confidence=scored.confidence,
            recommend_interview=scored.recommend_interview,
            disqualified=scored.disqualified,
            strengths=llm_result.get("strengths") or [],
            gaps=llm_result.get("gaps") or [],
            requirement_assessments=response_assessments,
            ai_model=self._evaluator.ai_model,
            prompt_version=settings.prompt_version,
            reasoning_summary=reasoning,
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
