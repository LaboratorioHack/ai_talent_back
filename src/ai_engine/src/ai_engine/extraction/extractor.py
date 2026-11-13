from ai_engine.anonymization.sanitizer import SanitizationReport
from ai_engine.core.config import get_settings
from ai_engine.core.llm import LlmClient
from ai_engine.schemas.cv import (
    AttributesSanitized,
    BiasGuardrailTelemetry,
    StructuredCandidateProfile,
)

_SYSTEM = """You are an expert HR data parsing and Responsible AI compliance engine.
Extract candidate experience, skills, and qualifications into the structured profile schema.

CRITICAL RESPONSIBLE AI RULES:
1. NEVER include demographic markers (age, gender, marital status, race/ethnicity,
   location/address, photo references, or university prestige/location) in summary,
   highlights, education, or other professional evaluation fields.
2. Education must be limited to degree, field of study, and level. Omit institution
   name and campus location. If you omit an institution, set compliance flag
   institution_name_omitted.
3. Evaluate only technical skill, years of relevant experience, project achievements,
   and education level. Infer technologies from project context, compute total
   experience months from date ranges, and estimate seniority (junior/mid/senior/staff).
   Do not invent employers, degrees, or achievements.
4. The CV text has already been regex-redacted (placeholders such as [EMAIL], [PHONE],
   [ID], [AGE], [DOB], [ADDRESS], [CANDIDATE]). In bias_guardrail_telemetry, count
   leftover demographic or PII indicators still present in this text (gendered
   pronouns, city/country names, university names, photo references, residual names
   or contact details). Do not recount tokens already replaced with placeholders.
   Add compliance_flags such as photo_reference, institution_name_omitted, and
   gendered_language when applicable. Keep pii_scrubbed true when any leftover
   indicator is found or placeholders are present.

COMPLETENESS (the profile is rendered back to the person as a full résumé):
- headline: the person's current or most recent job title, a few words.
- work_experience: EVERY position in the CV, most recent first, with title, company
  (leave it empty when the CV only shows a placeholder such as "Company Name"),
  start_date and end_date as written (use "Current" for an ongoing role), and the 3 to 6
  most important achievements as highlights, one concise sentence each. Never skip a
  position to save space; shorten the highlights instead.
- education: every degree, with degree, field, level (e.g. bachelor, master, doctorate,
  technical) and dates when the CV gives them.
- projects: notable named projects (name, one-sentence description, technologies).
- leadership: leadership roles, volunteering, teaching, communities and activities.
- awards: awards and recognitions, one line each.
- certifications and languages (with level when given) as lists of strings.
- skills: up to 30 concrete skills, tools and technologies, without generic words.
Leave a list empty when the CV has nothing for it. Do not invent anything.
"""

_RESIDUAL_FLAG = "residual_pii_detected"


async def extract_profile(
    anonymized_text: str,
    llm: LlmClient | None = None,
    sanitization: SanitizationReport | None = None,
) -> StructuredCandidateProfile:
    client = llm or LlmClient()
    settings = get_settings()
    profile = await client.complete_json(
        _SYSTEM,
        f"CV text:\n\n{anonymized_text[:120_000]}",
        StructuredCandidateProfile,
        # A long CV (several roles with their achievements) needs far more than a summary: with a
        # small budget the tool call is cut and the experience or education are lost.
        max_tokens=8192,
    )
    profile.extractor_version = settings.extractor_version
    profile.bias_guardrail_telemetry = _merge_telemetry(
        profile.bias_guardrail_telemetry,
        sanitization,
    )
    return profile


def _merge_telemetry(
    llm_telemetry: BiasGuardrailTelemetry | None,
    sanitization: SanitizationReport | None,
) -> BiasGuardrailTelemetry | None:
    if llm_telemetry is None and sanitization is None:
        return None

    llm_attrs = llm_telemetry.attributes_sanitized if llm_telemetry else AttributesSanitized()
    flags = list(llm_telemetry.compliance_flags) if llm_telemetry else []
    san = sanitization or SanitizationReport()

    merged = AttributesSanitized(
        names_removed=llm_attrs.names_removed + san.names_removed,
        contact_info_removed=llm_attrs.contact_info_removed + san.contact_info_removed,
        gender_indicators_detected=(
            llm_attrs.gender_indicators_detected + san.gender_indicators_detected
        ),
        location_indicators_removed=(
            llm_attrs.location_indicators_removed + san.location_indicators_removed
        ),
        age_or_dob_indicators_removed=(
            llm_attrs.age_or_dob_indicators_removed + san.age_or_dob_indicators_removed
        ),
    )

    residual = any(
        (
            llm_attrs.names_removed,
            llm_attrs.contact_info_removed,
            llm_attrs.gender_indicators_detected,
            llm_attrs.location_indicators_removed,
            llm_attrs.age_or_dob_indicators_removed,
        )
    )
    if residual and _RESIDUAL_FLAG not in flags:
        flags.append(_RESIDUAL_FLAG)

    llm_scrubbed = llm_telemetry.pii_scrubbed if llm_telemetry else False
    pii_scrubbed = sanitization is not None or llm_scrubbed
    return BiasGuardrailTelemetry(
        pii_scrubbed=pii_scrubbed,
        attributes_sanitized=merged,
        compliance_flags=flags,
    )
