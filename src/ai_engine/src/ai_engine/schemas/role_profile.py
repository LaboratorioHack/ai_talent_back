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
