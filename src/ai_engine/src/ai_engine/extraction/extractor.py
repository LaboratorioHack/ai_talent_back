from ai_engine.core.config import get_settings
from ai_engine.core.llm import LlmClient
from ai_engine.schemas.cv import StructuredCandidateProfile

_SYSTEM = """You extract structured, anonymized professional profile data from a CV.
Infer technologies from project context, compute total experience months from date ranges,
and estimate seniority (junior/mid/senior/staff). Do not invent employers or degrees."""


async def extract_profile(
    anonymized_text: str, llm: LlmClient | None = None
) -> StructuredCandidateProfile:
    client = llm or LlmClient()
    settings = get_settings()
    profile = await client.complete_json(
        _SYSTEM,
        f"CV text:\n\n{anonymized_text[:120_000]}",
        StructuredCandidateProfile,
    )
    profile.extractor_version = settings.extractor_version
    return profile
