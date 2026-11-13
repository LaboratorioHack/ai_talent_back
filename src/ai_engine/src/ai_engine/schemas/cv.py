from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field


class WorkExperience(BaseModel):
    title: str = ""
    company: str = ""
    start_date: str | None = None
    end_date: str | None = None
    months: int | None = None
    highlights: list[str] = Field(default_factory=list)
    technologies: list[str] = Field(default_factory=list)


class Education(BaseModel):
    """Degree without the institution (Responsible AI: no university prestige or location)."""

    model_config = ConfigDict(extra="allow")

    degree: str = ""
    field: str = ""
    level: str | None = None
    start_date: str | None = None
    end_date: str | None = None


class Project(BaseModel):
    name: str = ""
    description: str = ""
    technologies: list[str] = Field(default_factory=list)


class AttributesSanitized(BaseModel):
    names_removed: int = 0
    contact_info_removed: int = 0
    gender_indicators_detected: int = 0
    location_indicators_removed: int = 0
    age_or_dob_indicators_removed: int = 0


class BiasGuardrailTelemetry(BaseModel):
    pii_scrubbed: bool = False
    attributes_sanitized: AttributesSanitized = Field(default_factory=AttributesSanitized)
    compliance_flags: list[str] = Field(default_factory=list)


class StructuredCandidateProfile(BaseModel):
    headline: str = ""
    summary: str = ""
    total_experience_months: int | None = None
    seniority_level: str | None = None
    work_experience: list[WorkExperience] = Field(default_factory=list)
    education: list[Education] = Field(default_factory=list)
    projects: list[Project] = Field(default_factory=list)
    leadership: list[str] = Field(default_factory=list)
    awards: list[str] = Field(default_factory=list)
    skills: list[str] = Field(default_factory=list)
    certifications: list[str] = Field(default_factory=list)
    languages: list[str] = Field(default_factory=list)
    parser_strategy: str = "native_layout"
    ocr_confidence: float | None = None
    extractor_version: str = ""
    bias_guardrail_telemetry: BiasGuardrailTelemetry | None = None


class CvIngestRequest(BaseModel):
    resume_id: UUID
    candidate_id: UUID | None = None


class CvIngestResponse(BaseModel):
    resume_id: UUID
    extractor_version: str
    parser_strategy: str
    ocr_confidence: float | None
    chunks_count: int
    professional_data: StructuredCandidateProfile
