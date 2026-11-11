from uuid import UUID

from pydantic import BaseModel, Field


class EvidenceChunk(BaseModel):
    fragment_id: UUID
    section: str | None = None
    citation: str
    page_number: int | None = None
    similarity: float | None = None


class RequirementAssessment(BaseModel):
    requirement_id: UUID
    result: str
    score: float
    confidence: float
    justification: str
    evidence: list[EvidenceChunk] = Field(default_factory=list)


class EvaluateRequest(BaseModel):
    application_id: UUID | None = None
    job_opening_id: UUID | None = None
    candidate_id: UUID | None = None
    role_profile_id: UUID
    resume_id: UUID


class EvaluateResponse(BaseModel):
    evaluation_id: UUID
    total_score: float
    fulfillment: str
    confidence: float
    recommend_interview: bool
    disqualified: bool
    strengths: list[dict]
    gaps: list[dict]
    requirement_assessments: list[RequirementAssessment]
    ai_model: str
    prompt_version: str
    reasoning_summary: str


class RankCandidatesRequest(BaseModel):
    role_profile_id: UUID
    resume_ids: list[UUID]


class RankedCandidate(BaseModel):
    resume_id: UUID
    evaluation_id: UUID
    total_score: float
    confidence: float
    fulfillment: str
    recommend_interview: bool


class RankCandidatesResponse(BaseModel):
    role_profile_id: UUID
    ranked: list[RankedCandidate]


class EvidenceSearchRequest(BaseModel):
    resume_id: UUID
    query: str
    limit: int = 8


class EvidenceSearchResponse(BaseModel):
    resume_id: UUID
    query: str
    chunks: list[EvidenceChunk]
