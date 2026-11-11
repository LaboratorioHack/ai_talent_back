from ai_engine.rag.chunker import chunk_cv_text


def test_chunk_preserves_page_metadata() -> None:
    pages = [(1, "Experience\nBuilt APIs"), (2, "Education\nMSc")]
    chunks = chunk_cv_text("ignored", pages=pages)
    assert chunks
    assert chunks[0].source_location.get("page_number") == 1
