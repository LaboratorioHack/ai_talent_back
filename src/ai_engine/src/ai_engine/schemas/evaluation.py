from uuid import UUID

from pydantic import BaseModel, Field, field_validator


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


class FastPassInfo(BaseModel):
    used: bool = False
    skipped_claude: bool = False
    model: str | None = None
    certainty: float | None = None


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
    fast_pass: FastPassInfo = Field(default_factory=FastPassInfo)


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


class CompareCandidatesRequest(BaseModel):
    role_profile_id: UUID
    resume_ids: list[UUID] = Field(min_length=2, max_length=3)

    @field_validator("resume_ids")
    @classmethod
    def unique_resume_ids(cls, value: list[UUID]) -> list[UUID]:
        if len(set(value)) != len(value):
            raise ValueError("resume_ids must be unique")
        return value


class SideBySideCandidate(BaseModel):
    candidate_id: str
    primary_advantage: str
    primary_trade_off_or_gap: str
    ideal_team_fit: str


class InterviewFocus(BaseModel):
    candidate_id: str
    target_gap_to_verify: str
    suggested_interview_question: str


class ComparerResult(BaseModel):
    comparative_summary: str = ""
    side_by_side_matrix: list[SideBySideCandidate] = Field(default_factory=list)
    head_to_head_trade_off: str = ""
    recommended_interview_focus: list[InterviewFocus] = Field(default_factory=list)


class CompareCandidatesResponse(BaseModel):
    role_profile_id: UUID
    comparative_summary: str
    side_by_side_matrix: list[SideBySideCandidate]
    head_to_head_trade_off: str
    recommended_interview_focus: list[InterviewFocus]
