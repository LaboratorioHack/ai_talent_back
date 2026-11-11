import pytest
from fastapi.testclient import TestClient

from ai_engine.api.main import app
from ai_engine.core.config import get_settings


def test_scalar_route_in_development() -> None:
    if get_settings().is_production:
        pytest.skip("Scalar is disabled in production")
    response = TestClient(app).get("/scalar")
    assert response.status_code == 200
    assert "text/html" in response.headers.get("content-type", "")
