from ai_engine.rag.knowledge.taxonomy import expand_query_terms, transferability


def test_expand_query_adds_related_terms() -> None:
    expanded = expand_query_terms("postgresql experience")
    assert "aws" not in expanded.lower() or "postgresql" in expanded.lower()
    assert "Related terms:" in expanded


def test_transferability_same_cloud_family() -> None:
    assert transferability("aws", "amazon web services") >= 0.9
