"""AI drafting for the role-profile editor: proposed requirements and equivalences."""

from __future__ import annotations

from pydantic import ValidationError

from ai_engine.agents.profile_assistant.agent import ProfileAssistantAgent
from ai_engine.schemas.role_profile import (
    DraftVacancyRequest,
    DraftVacancyResponse,
    Equivalence,
    ExistingRequirement,
    ProposedRequirement,
    ProposeRequirementsRequest,
    ProposeRequirementsResponse,
    SuggestEquivalencesRequest,
    SuggestEquivalencesResponse,
    VacancyDraft,
)

MAX_REQUIREMENTS = 8
MAX_EQUIVALENCES = 4
MAX_TEXT = 80
FULL_WEIGHT = 100
MAX_DESCRIPTION = 4000
MAX_OPENINGS = 50
MAX_CLOSING_DAYS = 365


def _clean(text: str) -> str:
    return " ".join(text.split())[:MAX_TEXT].strip()


def _key(text: str) -> str:
    return _clean(text).casefold()


def weight_budget(existing: list[ExistingRequirement]) -> int:
    """Weight left for new requirements so the profile can still add up to 100."""
    used = sum(r.weight for r in existing if r.type != "DISQUALIFYING")
    return max(0, FULL_WEIGHT - used)


def clean_equivalences(items: list[Equivalence], exclude: set[str]) -> list[Equivalence]:
    seen = set(exclude)
    result: list[Equivalence] = []
    for item in items:
        label = _clean(item.label)
        if not label or _key(label) in seen:
            continue
        seen.add(_key(label))
        result.append(Equivalence(label=label, percent=min(100, max(1, item.percent))))
        if len(result) == MAX_EQUIVALENCES:
            break
    return result


def fit_weights(requirements: list[ProposedRequirement], budget: int) -> None:
    """Scales MANDATORY/DESIRABLE weights to add up to `budget` (largest remainder)."""
    weighted = [r for r in requirements if r.type != "DISQUALIFYING"]
    if not weighted:
        return
    raw = [max(0, r.weight) for r in weighted]
    if sum(raw) == 0:
        raw = [1] * len(raw)
    total = sum(raw)
    exact = [budget * w / total for w in raw]
    floors = [int(x) for x in exact]
    order = sorted(range(len(exact)), key=lambda i: exact[i] - floors[i], reverse=True)
    for i in order[: budget - sum(floors)]:
        floors[i] += 1
    for requirement, weight in zip(weighted, floors, strict=True):
        requirement.weight = weight


def _pick(value: str | None, options: list[str]) -> str | None:
    """The option that matches `value` (ignoring case), so the editor's selects can show it."""
    if not value:
        return None
    wanted = _key(value)
    return next((option for option in options if _key(option) == wanted), None)


def _optional(value: str | None) -> str | None:
    text = _clean(value or "")
    return text or None


def clean_requirements(items: list[ProposedRequirement]) -> list[ProposedRequirement]:
    known: set[str] = set()
    requirements: list[ProposedRequirement] = []
    for item in items:
        description = _clean(item.description)
        if not description or _key(description) in known:
            continue
        known.add(_key(description))
        requirements.append(
            ProposedRequirement(
                description=description,
                type=item.type,
                weight=0 if item.type == "DISQUALIFYING" else item.weight,
                equivalences=clean_equivalences(item.equivalences, {_key(description)}),
            )
        )
        if len(requirements) == MAX_REQUIREMENTS:
            break
    return requirements


def clean_draft(draft: VacancyDraft, request: DraftVacancyRequest) -> VacancyDraft:
    """Keeps the draft inside what the editor and the API accept: fixed lists, 1-50 openings,
    a future closing date, weights adding up to 100 and at least one mandatory requirement."""
    requirements = clean_requirements(draft.requirements)
    if requirements and not any(r.type == "MANDATORY" for r in requirements):
        first = next((r for r in requirements if r.type == "DESIRABLE"), None)
        if first is not None:
            first.type = "MANDATORY"
    fit_weights(requirements, FULL_WEIGHT)

    closes_at = draft.closes_at
    if closes_at is not None and not (0 < (closes_at - request.today).days <= MAX_CLOSING_DAYS):
        closes_at = None
    openings = draft.openings if draft.openings and 1 <= draft.openings <= MAX_OPENINGS else None

    return VacancyDraft(
        title=_clean(draft.title),
        area=_pick(draft.area, request.areas),
        description=" ".join(draft.description.split(" ")).strip()[:MAX_DESCRIPTION],
        openings=openings,
        closes_at=closes_at,
        location=_optional(draft.location),
        modality=draft.modality,
        contract=_pick(draft.contract, request.contracts),
        schedule=_pick(draft.schedule, request.schedules),
        salary=_optional(draft.salary),
        requirements=requirements,
    )


class RoleProfileService:
    def __init__(self, agent: ProfileAssistantAgent | None = None) -> None:
        self._agent = agent or ProfileAssistantAgent()

    async def propose(self, request: ProposeRequirementsRequest) -> ProposeRequirementsResponse:
        budget = weight_budget(request.existing)
        try:
            draft = await self._agent.propose(request, budget or FULL_WEIGHT)
        except ValidationError as exc:
            raise RuntimeError("The AI returned an invalid role profile.") from exc

        known = {_key(r.description) for r in request.existing}
        requirements: list[ProposedRequirement] = []
        for item in draft.requirements:
            description = _clean(item.description)
            if not description or _key(description) in known:
                continue
            known.add(_key(description))
            requirements.append(
                ProposedRequirement(
                    description=description,
                    type=item.type,
                    weight=0 if item.type == "DISQUALIFYING" else item.weight,
                    equivalences=clean_equivalences(item.equivalences, {_key(description)}),
                )
            )
            if len(requirements) == MAX_REQUIREMENTS:
                break

        if budget:
            fit_weights(requirements, budget)
        return ProposeRequirementsResponse(requirements=requirements)

    async def draft_vacancy(self, request: DraftVacancyRequest) -> DraftVacancyResponse:
        try:
            draft = await self._agent.draft_vacancy(request)
        except ValidationError as exc:
            raise RuntimeError("The AI returned an invalid vacancy draft.") from exc
        cleaned = clean_draft(draft, request)
        if not cleaned.title or not cleaned.requirements:
            raise RuntimeError("The AI could not draft a vacancy from this context.")
        return DraftVacancyResponse(draft=cleaned)

    async def suggest_equivalences(
        self, request: SuggestEquivalencesRequest
    ) -> SuggestEquivalencesResponse:
        try:
            draft = await self._agent.suggest_equivalences(request)
        except ValidationError as exc:
            raise RuntimeError("The AI returned invalid equivalences.") from exc

        exclude = {_key(request.requirement), *(_key(label) for label in request.existing)}
        return SuggestEquivalencesResponse(
            equivalences=clean_equivalences(draft.equivalences, exclude)
        )
