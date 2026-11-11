"""Redact PII before any LLM or embedding sees CV text."""

import re

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


def anonymize_cv_text(text: str, *, candidate_label: str = "[CANDIDATE]") -> str:
    redacted = text
    redacted = _EMAIL.sub("[EMAIL]", redacted)
    redacted = _PHONE.sub("[PHONE]", redacted)
    redacted = _DOC_ID.sub("[ID]", redacted)
    redacted = _AGE.sub("[AGE]", redacted)
    redacted = _DATE_BIRTH.sub("[DOB]", redacted)
    redacted = _ADDRESS.sub("[ADDRESS]", redacted)
    # Heuristic: first non-empty line often is the person's name on CVs.
    lines = redacted.splitlines()
    for idx, line in enumerate(lines[:3]):
        stripped = line.strip()
        if stripped and len(stripped.split()) <= 5 and stripped.isascii():
            lines[idx] = candidate_label
            break
    return "\n".join(lines)
