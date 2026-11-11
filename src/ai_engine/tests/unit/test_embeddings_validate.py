import pytest

from ai_engine.rag.embeddings.client import _validate_dim


def test_rejects_wrong_dimension(monkeypatch) -> None:
    monkeypatch.setenv("EMBEDDINGS_DIMENSIONS", "1024")
    from ai_engine.core.config import get_settings

    get_settings.cache_clear()
    with pytest.raises(ValueError):
        _validate_dim([0.0] * 512)
    get_settings.cache_clear()
