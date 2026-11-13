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


# 1–5 words made only of letters (any alphabet), joined by spaces, apostrophes or hyphens.
_NAME_LINE = re.compile(r"^[^\W\d_]+(?:[ '’-][^\W\d_]+){0,4}$")


@dataclass(frozen=True, slots=True)
class SanitizationReport:
    names_removed: int = 0
    contact_info_removed: int = 0
    gender_indicators_detected: int = 0
    location_indicators_removed: int = 0
    age_or_dob_indicators_removed: int = 0


def _find_name(text: str) -> str | None:
    for line in text.splitlines()[:3]:
        stripped = line.strip()
        if stripped and _NAME_LINE.match(stripped):
            return stripped
    return None


def _redact_name(text: str, name: str, label: str) -> str:
    return re.sub(re.escape(name), label, text, flags=re.IGNORECASE)


def _redact_contacts(text: str) -> tuple[str, dict[str, int]]:
    counts: dict[str, int] = {}
    for key, pattern, label in (
        ("email", _EMAIL, "[EMAIL]"),
        ("phone", _PHONE, "[PHONE]"),
        ("id", _DOC_ID, "[ID]"),
        ("age", _AGE, "[AGE]"),
        ("dob", _DATE_BIRTH, "[DOB]"),
        ("address", _ADDRESS, "[ADDRESS]"),
    ):
        text, counts[key] = pattern.subn(label, text)
    return text, counts


def anonymize_pages(
    raw_text: str, pages: list[tuple[int, str]], *, candidate_label: str = "[CANDIDATE]"
) -> list[tuple[int, str]]:
    """Same redaction for each page, with the name found at the top of the whole document.

    Fragments (and so the evidence quoted to recruiters) are cut from pages, so the pages must
    be anonymized too, not only the full text.
    """
    name = _find_name(_redact_contacts(raw_text)[0])
    result: list[tuple[int, str]] = []
    for number, text in pages:
        redacted, _counts = _redact_contacts(text)
        result.append((number, _redact_name(redacted, name, candidate_label) if name else redacted))
    return result


def anonymize_cv_text(text: str, *, candidate_label: str = "[CANDIDATE]") -> str:
    redacted, _report = anonymize_cv_text_with_report(text, candidate_label=candidate_label)
    return redacted


def anonymize_cv_text_with_report(
    text: str, *, candidate_label: str = "[CANDIDATE]"
) -> tuple[str, SanitizationReport]:
    redacted, counts = _redact_contacts(text)

    # Heuristic: one of the first lines is usually the person's name. Accented names count too
    # ("Laura Méndez Ocampo"); once found, the name is removed wherever else it appears.
    name = _find_name(redacted)
    names_removed = 0
    if name:
        redacted = _redact_name(redacted, name, candidate_label)
        names_removed = 1

    report = SanitizationReport(
        names_removed=names_removed,
        contact_info_removed=counts["email"] + counts["phone"] + counts["id"],
        location_indicators_removed=counts["address"],
        age_or_dob_indicators_removed=counts["age"] + counts["dob"],
    )
    return redacted, report
