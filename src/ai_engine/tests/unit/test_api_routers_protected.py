from fastapi import Depends, FastAPI
from fastapi.testclient import TestClient

from ai_engine.api.routers import cvs, evaluations, rag
from ai_engine.core.config import Settings, get_settings
from ai_engine.core.security import require_internal_api_key


def _app() -> FastAPI:
    app = FastAPI()
    protected = [Depends(require_internal_api_key)]
    app.include_router(cvs.router, dependencies=protected)
    app.include_router(evaluations.router, dependencies=protected)
    app.include_router(rag.router, dependencies=protected)
    app.dependency_overrides[get_settings] = lambda: Settings(engine_api_key="k")
    return app


def test_business_routes_require_api_key() -> None:
    client = TestClient(_app())
    assert (
        client.post(
            "/v1/cvs/ingest-json", json={"resume_id": "00000000-0000-0000-0000-000000000001"}
        ).status_code
        == 401
    )
    headers = {"X-Internal-Api-Key": "k"}
    # Will fail at service layer without DB, but passes auth
    assert (
        client.post(
            "/v1/rag/evidence",
            json={"resume_id": "00000000-0000-0000-0000-000000000001", "query": "aws"},
            headers=headers,
        ).status_code
        != 401
    )
    compare_body = {
        "role_profile_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        "resume_ids": [
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
        ],
    }
    assert client.post("/v1/evaluations/compare", json=compare_body).status_code == 401
    assert (
        client.post("/v1/evaluations/compare", json=compare_body, headers=headers).status_code
        != 401
    )
