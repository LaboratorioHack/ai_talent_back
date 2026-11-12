from unittest.mock import AsyncMock, MagicMock

import pytest

from ai_engine.anonymization.sanitizer import SanitizationReport
from ai_engine.extraction.extractor import extract_profile
from ai_engine.schemas.cv import (
    AttributesSanitized,
    BiasGuardrailTelemetry,
    StructuredCandidateProfile,
)


@pytest.mark.asyncio
async def test_extract_profile_merges_sanitizer_and_llm_counts() -> None:
    llm = MagicMock()
    llm.complete_json = AsyncMock(
        return_value=StructuredCandidateProfile(
            summary="Backend engineer with cloud experience.",
            bias_guardrail_telemetry=BiasGuardrailTelemetry(
                pii_scrubbed=False,
                attributes_sanitized=AttributesSanitized(
                    gender_indicators_detected=2,
                    location_indicators_removed=1,
                ),
                compliance_flags=["gendered_language"],
            ),
        )
    )
    sanitization = SanitizationReport(
        names_removed=1,
        contact_info_removed=2,
        location_indicators_removed=1,
        age_or_dob_indicators_removed=1,
    )

    profile = await extract_profile("redacted cv", llm=llm, sanitization=sanitization)

    telemetry = profile.bias_guardrail_telemetry
    assert telemetry is not None
    assert telemetry.pii_scrubbed is True
    assert telemetry.attributes_sanitized.names_removed == 1
    assert telemetry.attributes_sanitized.contact_info_removed == 2
    assert telemetry.attributes_sanitized.gender_indicators_detected == 2
    assert telemetry.attributes_sanitized.location_indicators_removed == 2
    assert telemetry.attributes_sanitized.age_or_dob_indicators_removed == 1
    assert "gendered_language" in telemetry.compliance_flags
    assert "residual_pii_detected" in telemetry.compliance_flags
    llm.complete_json.assert_awaited_once()
