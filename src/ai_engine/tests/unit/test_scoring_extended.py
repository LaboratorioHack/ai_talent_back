from ai_engine.scoring.calculator import RequirementScoreInput, compute_scores


def test_recommend_interview_when_meets_threshold() -> None:
    items = [
        RequirementScoreInput("MANDATORY", 1, "MEETS", 85, 0.9),
        RequirementScoreInput("DESIRABLE", 1, "MEETS", 80, 0.85),
    ]
    result = compute_scores(items, ocr_confidence=0.9)
    assert result.fulfillment == "MEETS"
    assert result.recommend_interview is True
    assert result.confidence >= 0.85
