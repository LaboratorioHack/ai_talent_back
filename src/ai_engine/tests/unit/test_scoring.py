from ai_engine.scoring.calculator import RequirementScoreInput, compute_scores


def test_mandatory_failure_caps_recommendation() -> None:
    items = [
        RequirementScoreInput("MANDATORY", 1, "DOES_NOT_MEET", 20, 0.8),
        RequirementScoreInput("DESIRABLE", 1, "MEETS", 90, 0.9),
    ]
    result = compute_scores(items, ocr_confidence=0.9)
    assert result.fulfillment == "DOES_NOT_MEET"
    assert result.recommend_interview is False


def test_disqualifying_short_circuits() -> None:
    items = [
        RequirementScoreInput("DISQUALIFYING", 1, "MEETS", 100, 0.95),
        RequirementScoreInput("MANDATORY", 1, "MEETS", 100, 0.95),
    ]
    result = compute_scores(items, ocr_confidence=None)
    assert result.disqualified is True
    assert result.total_score == 0
