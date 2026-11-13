"""Drafts role-profile requirements and skill equivalences from a job description."""

from __future__ import annotations

import json

from ai_engine.core.llm import LlmClient
from ai_engine.schemas.role_profile import (
    DraftVacancyRequest,
    ProposeRequirementsRequest,
    ProposeRequirementsResponse,
    SuggestEquivalencesRequest,
    SuggestEquivalencesResponse,
    VacancyDraft,
)

_RULES = """
CRITICAL RULES:
1. Only propose criteria that can be verified from a CV: skills, tools, years of experience,
   education, certifications, languages, domain experience.
2. NEVER use protected attributes or their proxies: age, gender, nationality, ethnicity,
   marital status, religion, health, disability, photo, address or neighborhood, or the
   prestige of a specific university.
3. Write every text in the same language as the job description (Spanish by default).
   Keep each description and label short (at most 80 characters).
4. Equivalences are alternative skills, tools or certifications that partially cover a
   requirement. `percent` (1-100) is how much of the requirement they cover, e.g. for
   "Azure": AWS 80, Google Cloud 70. At most 4 per requirement; return an empty list when
   no alternative makes sense (languages, years of experience, legal licenses).
"""

_PROPOSE_SYSTEM = (
    """You are an expert technical recruiter who turns a job description into a structured,
weighted role profile used to screen CVs fairly.
"""
    + _RULES
    + """5. type: MANDATORY for explicit must-haves, DESIRABLE for nice-to-haves, DISQUALIFYING
   only for explicit, legal and job-related exclusions (rare; usually none).
6. weight: integer. MANDATORY and DESIRABLE weights must add up to `weight_budget`;
   MANDATORY weights should be higher than DESIRABLE ones. DISQUALIFYING weight is 0.
7. Do not repeat or rephrase any requirement listed in `existing_requirements`.
8. Propose between 3 and 8 requirements, the most decisive first.
"""
)

_DRAFT_SYSTEM = (
    """You are an expert technical recruiter. The recruiter gives you free context about an
opening (a pasted job ad, notes, or a few loose lines). Fill in the whole vacancy so they only
review it.
"""
    + _RULES
    + """5. title: the job title, at most 80 characters, without the company name.
6. description: 2 to 5 short paragraphs for candidates, at most 1500 characters: what the
   person will do, with whom, and what they need to know. Use only what the context says or
   clearly implies; never invent benefits, salary or company facts.
7. area: exactly one value from `areas`, or null when none fits.
8. openings: the number of people to hire (1 to 50) when the context says it; otherwise null.
9. closes_at: an ISO date after `today` when the context gives a deadline; otherwise null.
10. location, modality (ONSITE, HYBRID or REMOTE), contract (one of `contracts`), schedule
    (one of `schedules`) and salary (as written, e.g. "$5.000.000 - $6.500.000 COP"): only
    when the context states them; otherwise null.
11. requirements: 3 to 8, the most decisive first. type MANDATORY for explicit must-haves,
    DESIRABLE for nice-to-haves, DISQUALIFYING only for explicit, legal and job-related
    exclusions (rare). MANDATORY and DESIRABLE weights are integers adding up to 100, with
    MANDATORY weights higher. DISQUALIFYING weight is 0. Include at least one MANDATORY.
"""
)

_EQUIVALENCES_SYSTEM = (
    """You are an expert technical recruiter. Suggest equivalences for ONE requirement of a
role profile, using the job context when given.
"""
    + _RULES
    + """5. Do not repeat any label listed in `existing_equivalences`.
"""
)


class ProfileAssistantAgent:
    def __init__(self, llm: LlmClient | None = None) -> None:
        self._llm = llm or LlmClient()

    async def propose(
        self, request: ProposeRequirementsRequest, weight_budget: int
    ) -> ProposeRequirementsResponse:
        payload = {
            "job_title": request.title,
            "area": request.area,
            "job_description": request.description,
            "existing_requirements": [r.description for r in request.existing],
            "weight_budget": weight_budget,
        }
        return await self._llm.complete_json(
            _PROPOSE_SYSTEM,
            json.dumps(payload, ensure_ascii=False),
            ProposeRequirementsResponse,
            max_tokens=2048,
            tool_name="propose_requirements",
            tool_description="Weighted requirements proposed for the role profile.",
        )

    async def draft_vacancy(self, request: DraftVacancyRequest) -> VacancyDraft:
        payload = {
            "context": request.context,
            "today": request.today.isoformat(),
            "areas": request.areas,
            "contracts": request.contracts,
            "schedules": request.schedules,
        }
        return await self._llm.complete_json(
            _DRAFT_SYSTEM,
            json.dumps(payload, ensure_ascii=False),
            VacancyDraft,
            max_tokens=4096,
            tool_name="draft_vacancy",
            tool_description="The whole vacancy drafted from the recruiter's context.",
        )

    async def suggest_equivalences(
        self, request: SuggestEquivalencesRequest
    ) -> SuggestEquivalencesResponse:
        payload = {
            "requirement": request.requirement,
            "job_title": request.title,
            "job_description": request.description,
            "existing_equivalences": request.existing,
        }
        return await self._llm.complete_json(
            _EQUIVALENCES_SYSTEM,
            json.dumps(payload, ensure_ascii=False),
            SuggestEquivalencesResponse,
            max_tokens=1024,
            tool_name="suggest_equivalences",
            tool_description="Alternative skills that partially cover the requirement.",
        )
