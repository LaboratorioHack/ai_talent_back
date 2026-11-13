import uuid

import pytest

from ai_engine.services.assessment_parsing import (
    MISSING_JUSTIFICATION,
    as_confidence,
    as_points,
    as_result,
    as_score,
    parse_assessments,
)


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        (0.8, 0.8),
        ("0.7", 0.7),
        ("high", 0.85),
        ("Medium", 0.6),
        ("low", 0.35),
        (85, 0.85),
        ("90%", 0.9),
        (None, 0.5),
        ("??", 0.5),
    ],
)
def test_confidence_accepts_numbers_percentages_and_words(value, expected):
    assert as_confidence(value) == pytest.approx(expected)


@pytest.mark.parametrize(
    ("value", "expected"), [(82, 82.0), ("75", 75.0), ("120", 100.0), (-3, 0.0), ("n/a", 0.0)]
)
def test_score_is_clamped_to_0_100(value, expected):
    assert as_score(value) == expected


@pytest.mark.parametrize(
    ("value", "expected"),
    [
        ("meets", "MEETS"),
        ("PARTIAL", "PARTIALLY_MEETS"),
        ("partially-meets", "PARTIALLY_MEETS"),
        ("does not meet", "DOES_NOT_MEET"),
        ("whatever", "NO_EVIDENCE"),
    ],
)
def test_result_is_normalized(value, expected):
    assert as_result(value) == expected


def test_one_assessment_per_requirement_in_order_and_missing_ones_flagged():
    a, b, c = uuid.uuid4(), uuid.uuid4(), uuid.uuid4()
    raw = [
        {
            "requirement_id": str(b),
            "result": "meets",
            "score": "90",
            "confidence": "high",
            "fragment_ids": [str(uuid.uuid4()), "nope"],
        },
        {"requirement_id": "not-a-uuid", "result": "MEETS"},
        {"requirement_id": str(uuid.uuid4()), "result": "MEETS"},
        {
            "requirement_id": str(a),
            "result": "PARTIAL",
            "score": 55,
            "confidence": 0.6,
            "justification": "Algo de Azure",
        },
    ]

    parsed = parse_assessments(raw, [a, b, c])

    assert [p.requirement_id for p in parsed] == [a, b, c]
    assert (parsed[0].result, parsed[0].score, parsed[0].justification) == (
        "PARTIALLY_MEETS",
        55.0,
        "Algo de Azure",
    )
    assert (parsed[1].result, parsed[1].score, parsed[1].confidence) == ("MEETS", 90.0, 0.85)
    assert len(parsed[1].fragment_ids) == 1
    assert (
        parsed[2].missing
        and parsed[2].result == "NO_EVIDENCE"
        and parsed[2].justification == MISSING_JUSTIFICATION
    )


def test_non_list_output_yields_no_evidence_for_every_requirement():
    req = uuid.uuid4()
    assert parse_assessments({"oops": True}, [req])[0].result == "NO_EVIDENCE"


def test_points_turn_strings_into_objects_and_drop_noise():
    assert as_points(["Azure", {"description": "AKS"}, "  ", 3]) == [
        {"description": "Azure"},
        {"description": "AKS"},
    ]
    assert as_points("no es lista") == []
