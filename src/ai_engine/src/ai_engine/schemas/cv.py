from typing import Any
from uuid import UUID

from pydantic import BaseModel, Field


class WorkExperience(BaseModel):
    title: str = ""
    company: str = ""
    start_date: str | None = None
    end_date: str | None = None
    months: int | None = None
    highlights: list[str] = Field(default_factory=list)
    technologies: list[str] = Field(default_factory=list)


class StructuredCandidateProfile(BaseModel):
    summary: str = ""
    total_experience_months: int | None = None
    seniority_level: str | None = None
    work_experience: list[WorkExperience] = Field(default_factory=list)
    education: list[dict[str, Any]] = Field(default_factory=list)
    skills: list[str] = Field(default_factory=list)
    certifications: list[str] = Field(default_factory=list)
    languages: list[str] = Field(default_factory=list)
    parser_strategy: str = "native_layout"
    ocr_confidence: float | None = None
    extractor_version: str = ""


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
