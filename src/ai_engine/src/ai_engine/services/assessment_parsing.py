"""Tolerant parsing of the evaluator's JSON.

LLM output is not a contract: a model may write ``"confidence": "high"``, ``"score": "85"``,
``"result": "PARTIAL"`` or skip a requirement. These helpers turn that into the values the
scoring expects instead of failing the whole evaluation (which used to return HTTP 400 or
save an evaluation with no requirement results).
"""

from __future__ import annotations

import uuid
from dataclasses import dataclass, field
from typing import Any

_WORD_CONFIDENCE = {
    "very high": 0.95,
    "high": 0.85,
    "medium": 0.6,
    "moderate": 0.6,
    "low": 0.35,
    "very low": 0.2,
}
_RESULTS = {
    "MEETS": "MEETS",
    "MET": "MEETS",
    "PARTIALLY_MEETS": "PARTIALLY_MEETS",
    "PARTIAL": "PARTIALLY_MEETS",
    "PARTIALLY MEETS": "PARTIALLY_MEETS",
    "DOES_NOT_MEET": "DOES_NOT_MEET",
    "NOT_MET": "DOES_NOT_MEET",
    "DOES NOT MEET": "DOES_NOT_MEET",
    "NO_EVIDENCE": "NO_EVIDENCE",
    "NO EVIDENCE": "NO_EVIDENCE",
}
MISSING_JUSTIFICATION = (
    "El modelo no devolvió una evaluación para este requisito; se trata como sin evidencia."
)


def as_confidence(value: Any, default: float = 0.5) -> float:
    """0–1. Accepts numbers, numeric strings, percentages (85 → 0.85) and words (high, low)."""
    if isinstance(value, str):
        word = value.strip().lower().rstrip("%")
        if word in _WORD_CONFIDENCE:
            return _WORD_CONFIDENCE[word]
        value = word
    try:
        number = float(value)
    except (TypeError, ValueError):
        return default
    if number > 1:
        number /= 100
    return min(1.0, max(0.0, number))


def as_score(value: Any, default: float = 0.0) -> float:
    """0–100. Accepts numbers and numeric strings; clamps out-of-range values."""
    try:
        number = float(str(value).strip().rstrip("%")) if isinstance(value, str) else float(value)
    except (TypeError, ValueError):
        return default
    return min(100.0, max(0.0, number))


def as_result(value: Any) -> str:
    key = str(value or "").strip().upper().replace("-", "_")
    return _RESULTS.get(key, _RESULTS.get(key.replace("_", " "), "NO_EVIDENCE"))


def as_uuid(value: Any) -> uuid.UUID | None:
    try:
        return uuid.UUID(str(value))
    except (TypeError, ValueError, AttributeError):
        return None


@dataclass(frozen=True)
class ParsedAssessment:
    requirement_id: uuid.UUID
    result: str
    score: float
    confidence: float
    justification: str
    fragment_ids: list[uuid.UUID] = field(default_factory=list)
    missing: bool = False


def parse_assessments(raw: Any, requirement_ids: list[uuid.UUID]) -> list[ParsedAssessment]:
    """One assessment per requirement, in requirement order.

    Unknown ids are ignored; skipped requirements become NO_EVIDENCE (flagged ``missing``).
    """
    by_id: dict[uuid.UUID, ParsedAssessment] = {}
    for item in raw if isinstance(raw, list) else []:
        if not isinstance(item, dict):
            continue
        req_id = as_uuid(item.get("requirement_id"))
        if req_id is None or req_id not in requirement_ids or req_id in by_id:
            continue
        by_id[req_id] = ParsedAssessment(
            requirement_id=req_id,
            result=as_result(item.get("result")),
            score=as_score(item.get("score")),
            confidence=as_confidence(item.get("confidence")),
            justification=str(item.get("justification") or ""),
            fragment_ids=[
                f for f in (as_uuid(x) for x in item.get("fragment_ids") or []) if f is not None
            ],
        )
    return [
        by_id.get(req_id)
        or ParsedAssessment(req_id, "NO_EVIDENCE", 0.0, 0.3, MISSING_JUSTIFICATION, missing=True)
        for req_id in requirement_ids
    ]
