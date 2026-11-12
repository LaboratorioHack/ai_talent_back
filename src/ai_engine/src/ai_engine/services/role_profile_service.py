"""AI drafting for the role-profile editor: proposed requirements and equivalences."""

from __future__ import annotations

from pydantic import ValidationError

from ai_engine.agents.profile_assistant.agent import ProfileAssistantAgent
from ai_engine.schemas.role_profile import (
    Equivalence,
    ExistingRequirement,
    ProposedRequirement,
    ProposeRequirementsRequest,
    ProposeRequirementsResponse,
    SuggestEquivalencesRequest,
    SuggestEquivalencesResponse,
)

MAX_REQUIREMENTS = 8
MAX_EQUIVALENCES = 4
MAX_TEXT = 80
FULL_WEIGHT = 100


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
