from __future__ import annotations

from dataclasses import dataclass


@dataclass
class RequirementScoreInput:
    type: str
    weight: float
    result: str
    score: float
    confidence: float


@dataclass
class ScoreResult:
    total_score: float
    fulfillment: str
    disqualified: bool
    confidence: float
    recommend_interview: bool


def compute_scores(
    items: list[RequirementScoreInput], *, ocr_confidence: float | None
) -> ScoreResult:
    if not items:
        return ScoreResult(0, "DOES_NOT_MEET", False, 0.2, False)

    disqualified = any(i.type == "DISQUALIFYING" and i.result == "MEETS" for i in items)
    mandatory = [i for i in items if i.type == "MANDATORY"]
    desirable = [i for i in items if i.type == "DESIRABLE"]

    m_score = _weighted(mandatory) if mandatory else 50.0
    d_score = _weighted(desirable) if desirable else m_score
    total = 0.7 * m_score + 0.3 * d_score

    if disqualified:
        return ScoreResult(
            0, "DOES_NOT_MEET", True, _aggregate_confidence(items, ocr_confidence), False
        )

    fulfillment = _fulfillment(total, mandatory)
    confidence = _aggregate_confidence(items, ocr_confidence)
    recommend = fulfillment == "MEETS" or (
        total >= 75 and confidence >= 0.65 and fulfillment != "DOES_NOT_MEET"
    )

    if mandatory and any(i.result == "DOES_NOT_MEET" for i in mandatory):
        recommend = False
        if fulfillment == "MEETS":
            fulfillment = "PARTIALLY_MEETS"

    return ScoreResult(round(total, 2), fulfillment, False, round(confidence, 3), recommend)


def _weighted(items: list[RequirementScoreInput]) -> float:
    if not items:
        return 0.0
    total_w = sum(i.weight for i in items) or len(items)
    return sum(i.score * (i.weight or 1) for i in items) / total_w


def _fulfillment(total: float, mandatory: list[RequirementScoreInput]) -> str:
    if mandatory and any(i.result == "DOES_NOT_MEET" for i in mandatory):
        return "DOES_NOT_MEET"
    if total >= 80:
        return "MEETS"
    if total >= 55:
        return "PARTIALLY_MEETS"
    return "DOES_NOT_MEET"


def _aggregate_confidence(
    items: list[RequirementScoreInput], ocr_confidence: float | None
) -> float:
    base = sum(i.confidence for i in items) / len(items) if items else 0.5
    if ocr_confidence is not None:
        return min(1.0, 0.7 * base + 0.3 * ocr_confidence)
    return min(1.0, base)
