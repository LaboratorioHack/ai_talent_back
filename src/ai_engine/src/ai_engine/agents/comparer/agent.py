"""Comparative trade-off analysis of 2–3 anonymized candidates for a vacancy."""

from __future__ import annotations

import json
from typing import Any

from ai_engine.core.llm import LlmClient
from ai_engine.schemas.evaluation import ComparerResult

_SYSTEM = """You are an executive talent acquisition strategist and technical assessment expert.
Your goal is to perform a comparative trade-off analysis between 2 to 3 top candidates for a
specific job vacancy. Analyze their extracted anonymized profiles against the job requirements
and identify key trade-offs, strengths, and candidate-specific interview verification questions.

CRITICAL RULES:
1. Focus strictly on technical capabilities, architectural experience, domain depth, and skill gaps.
2. Do NOT infer or mention protected attributes (age, gender, origin, etc.).
3. Provide sharp, actionable insights that help a hiring manager decide who to interview first
   based on team needs.
4. candidate_id MUST be the resume_id string from the input. Do not invent names or identifiers.
"""

_TOOL_DESCRIPTION = (
    "Comparative trade-off analysis of anonymized candidates against a vacancy."
)


class ComparerAgent:
    def __init__(self, llm: LlmClient | None = None) -> None:
        self._llm = llm or LlmClient()

    async def compare(
        self, job_vacancy: dict[str, Any], candidates: list[dict[str, Any]]
    ) -> ComparerResult:
        payload = {
            "job_description_and_requirements": job_vacancy,
            "candidates": candidates,
        }
        return await self._llm.complete_json(
            _SYSTEM,
            json.dumps(payload, default=str)[:100_000],
            ComparerResult,
            max_tokens=4096,
            tool_name="compare_candidates",
            tool_description=_TOOL_DESCRIPTION,
        )
