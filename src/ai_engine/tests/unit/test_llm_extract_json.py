from ai_engine.core.llm import _extract_json


def test_extract_json_from_markdown_fence() -> None:
    raw = 'Here is the result:\n```json\n{"score": 90, "ok": true}\n```'
    data = _extract_json(raw)
    assert data["score"] == 90
    assert data["ok"] is True
