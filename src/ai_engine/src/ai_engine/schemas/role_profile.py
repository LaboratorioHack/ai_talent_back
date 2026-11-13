from datetime import date
from typing import Literal

from pydantic import BaseModel, Field

RequirementType = Literal["MANDATORY", "DESIRABLE", "DISQUALIFYING"]


class Equivalence(BaseModel):
    label: str
    percent: int


class ExistingRequirement(BaseModel):
    description: str = Field(min_length=1, max_length=300)
    type: RequirementType = "MANDATORY"
    weight: int = Field(default=0, ge=0, le=100)


class ProposeRequirementsRequest(BaseModel):
    description: str = Field(min_length=40, max_length=4000)
    title: str | None = Field(default=None, max_length=200)
    area: str | None = Field(default=None, max_length=120)
    existing: list[ExistingRequirement] = Field(default_factory=list, max_length=50)


class ProposedRequirement(BaseModel):
    description: str
    type: RequirementType
    weight: int
    equivalences: list[Equivalence] = Field(default_factory=list)


class ProposeRequirementsResponse(BaseModel):
    requirements: list[ProposedRequirement]


class SuggestEquivalencesRequest(BaseModel):
    requirement: str = Field(min_length=2, max_length=300)
    title: str | None = Field(default=None, max_length=200)
    description: str | None = Field(default=None, max_length=4000)
    existing: list[str] = Field(default_factory=list, max_length=20)


class SuggestEquivalencesResponse(BaseModel):
    equivalences: list[Equivalence]


Modality = Literal["ONSITE", "HYBRID", "REMOTE"]


class DraftVacancyRequest(BaseModel):
    """Free context (a pasted job ad, notes, a few lines) to turn into a whole vacancy."""

    context: str = Field(min_length=20, max_length=8000)
    today: date
    # Fixed product lists the editor offers: the draft picks from them or leaves the field empty.
    areas: list[str] = Field(default_factory=list, max_length=30)
    contracts: list[str] = Field(default_factory=list, max_length=20)
    schedules: list[str] = Field(default_factory=list, max_length=20)


class VacancyDraft(BaseModel):
    title: str = ""
    area: str | None = None
    description: str = ""
    openings: int | None = None
    closes_at: date | None = None
    location: str | None = None
    modality: Modality | None = None
    contract: str | None = None
    schedule: str | None = None
    salary: str | None = None
    requirements: list[ProposedRequirement] = Field(default_factory=list)


class DraftVacancyResponse(BaseModel):
    draft: VacancyDraft
