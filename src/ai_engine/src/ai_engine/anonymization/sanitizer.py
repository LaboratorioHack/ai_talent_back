"""Redact PII before any LLM or embedding sees CV text."""

from __future__ import annotations

import re
from dataclasses import dataclass

_EMAIL = re.compile(r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")
_PHONE = re.compile(
    r"(?:\+?\d{1,3}[\s-]?)?(?:\(?\d{2,4}\)?[\s-]?)?\d{3,4}[\s-]?\d{3,4}(?:[\s-]?\d{2,4})?"
)
_DOC_ID = re.compile(r"\b\d{6,12}\b")
_AGE = re.compile(r"\b(?:edad|age)\s*[:.]?\s*\d{1,3}\b", re.IGNORECASE)
_DATE_BIRTH = re.compile(
    r"\b(?:nacimiento|fecha de nacimiento|dob|date of birth)\s*[:.]?\s*[\d/.\-]+\b",
    re.IGNORECASE,
)
_ADDRESS = re.compile(
    r"\b(?:direcci[oó]n|address|domicilio)\s*[:.]?\s*.+$",
    re.IGNORECASE | re.MULTILINE,
)


@dataclass(frozen=True, slots=True)
class SanitizationReport:
    names_removed: int = 0
    contact_info_removed: int = 0
    gender_indicators_detected: int = 0
    location_indicators_removed: int = 0
    age_or_dob_indicators_removed: int = 0


def anonymize_cv_text(text: str, *, candidate_label: str = "[CANDIDATE]") -> str:
    redacted, _report = anonymize_cv_text_with_report(text, candidate_label=candidate_label)
    return redacted


def anonymize_cv_text_with_report(
    text: str, *, candidate_label: str = "[CANDIDATE]"
) -> tuple[str, SanitizationReport]:
    redacted, email_n = _EMAIL.subn("[EMAIL]", text)
    redacted, phone_n = _PHONE.subn("[PHONE]", redacted)
    redacted, id_n = _DOC_ID.subn("[ID]", redacted)
    redacted, age_n = _AGE.subn("[AGE]", redacted)
    redacted, dob_n = _DATE_BIRTH.subn("[DOB]", redacted)
    redacted, address_n = _ADDRESS.subn("[ADDRESS]", redacted)

    names_removed = 0
    # Heuristic: first non-empty line often is the person's name on CVs.
    lines = redacted.splitlines()
    for idx, line in enumerate(lines[:3]):
        stripped = line.strip()
        if stripped and len(stripped.split()) <= 5 and stripped.isascii():
            lines[idx] = candidate_label
            names_removed = 1
            break
    redacted = "\n".join(lines)

    report = SanitizationReport(
        names_removed=names_removed,
        contact_info_removed=email_n + phone_n + id_n,
        location_indicators_removed=address_n,
        age_or_dob_indicators_removed=age_n + dob_n,
    )
    return redacted, report
