"""Requirement-level evaluation with grounded evidence (Claude + MAF-compatible wrapper)."""

from __future__ import annotations

import json
import uuid
from typing import Any

from pydantic import BaseModel, Field

from ai_engine.core.config import get_settings
from ai_engine.core.llm import LlmClient
from ai_engine.rag.knowledge.taxonomy import transferability
from ai_engine.rag.retrieval.evidence_retriever import RetrievedEvidence

_PROMPT_VERSION = "evaluator-v2"

_SYSTEM = (
    "You are a senior technical recruiter evaluating anonymized CV evidence "
    "against job requirements. Go beyond keywords: infer depth from achievements, "
    "tenure, and tech transferability. Every assessment MUST cite resume_fragment_id "
    "values from the evidence list. Output JSON with assessments, strengths, gaps, "
    "and reasoning_summary. For DISQUALIFYING requirements, result MEETS only when "
    "the disqualifying condition is clearly present in evidence.\n\n"
    "Return ONLY this JSON object, with one assessment for EVERY requirement:\n"
    '{"assessments": [{"requirement_id": "<id copied from requirements>", '
    '"result": "MEETS" | "PARTIALLY_MEETS" | "DOES_NOT_MEET" | "NO_EVIDENCE", '
    '"score": <number 0-100>, "confidence": <number 0-1>, '
    '"justification": "<one sentence in Spanish>", "fragment_ids": ["<resume_fragment_id>"]}], '
    '"strengths": ["<short Spanish phrase>"], "gaps": ["<short Spanish phrase>"], '
    '"reasoning_summary": "<two sentences in Spanish>"}\n'
    "Numbers must be JSON numbers, never words like high or low."
)


class EvaluatorAgent:
    def __init__(self, llm: LlmClient | None = None) -> None:
        self._llm = llm or LlmClient()

    @property
    def prompt_version(self) -> str:
        return _PROMPT_VERSION

    async def evaluate_requirements(
        self,
        requirements: list[dict[str, Any]],
        evidence_by_req: dict[uuid.UUID, list[RetrievedEvidence]],
        profile: dict[str, Any],
    ) -> dict[str, Any]:
        context = {
            "requirements": requirements,
            "professional_data": profile,
            "evidence": _serialize_evidence(evidence_by_req),
            "transferability_notes": _transfer_notes(requirements),
        }
        raw = await self._llm.complete_text(
            _SYSTEM,
            json.dumps(context, default=str)[:100_000],
            max_tokens=8192,
        )
        from ai_engine.core.llm import _extract_json  # noqa: PLC0415

        return _extract_json(raw)

    @property
    def ai_model(self) -> str:
        return get_settings().anthropic_model


def _serialize_evidence(evidence_by_req: dict[uuid.UUID, list[RetrievedEvidence]]) -> list[dict]:
    rows: list[dict] = []
    for req_id, chunks in evidence_by_req.items():
        for chunk in chunks:
            rows.append(
                {
                    "requirement_id": str(req_id),
                    "resume_fragment_id": str(chunk.fragment_id),
                    "section": chunk.section,
                    "page_number": chunk.page_number,
                    "similarity": chunk.similarity,
                    "quote": chunk.citation[:400],
                }
            )
    return rows


def _transfer_notes(requirements: list[dict[str, Any]]) -> list[str]:
    notes: list[str] = []
    for req in requirements:
        condition = (req.get("condition") or req.get("description") or "").lower()
        for token in condition.replace("/", " ").split():
            if len(token) < 3:
                continue
            for other in ("aws", "azure", "gcp", "java", "csharp", "react"):
                score = transferability(token, other)
                if 0 < score < 1:
                    notes.append(f"{token}~{other}:{score}")
    return notes[:30]


class AssessmentBundle(BaseModel):
    assessments: list[dict] = Field(default_factory=list)
    strengths: list[dict] = Field(default_factory=list)
    gaps: list[dict] = Field(default_factory=list)
    reasoning_summary: str = ""
