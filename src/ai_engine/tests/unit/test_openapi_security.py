from ai_engine.api.main import app
from ai_engine.core.config import Settings, get_settings


def test_openapi_marks_v1_routes_with_internal_api_key() -> None:
    app.dependency_overrides[get_settings] = lambda: Settings(environment="development")
    schema = app.openapi()
    app.dependency_overrides.clear()

    schemes = schema["components"]["securitySchemes"]
    assert "InternalApiKey" in schemes
    assert schemes["InternalApiKey"]["name"] == "X-Internal-Api-Key"

    ingest = schema["paths"]["/v1/cvs/ingest-json"]["post"]
    assert ingest["security"] == [{"InternalApiKey": []}]
