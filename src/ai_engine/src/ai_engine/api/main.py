import logging
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Request
from fastapi.responses import JSONResponse
from scalar_fastapi import add_scalar_reference

from ai_engine.api.openapi import configure_openapi
from ai_engine.api.routers import assistant, cvs, evaluations, health, rag, readiness, role_profiles
from ai_engine.core.config import get_settings
from ai_engine.core.security import require_internal_api_key
from ai_engine.persistence.db import close_pool

logger = logging.getLogger("ai_engine")
settings = get_settings()


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    try:
        yield
    finally:
        await close_pool()


# OWASP A02: no Swagger/ReDoc/Scalar in production.
app = FastAPI(
    title="AI Talent Scout - AI Engine",
    description="Ingesta y anonimización de CVs, extracción, RAG y agentes de evaluación.",
    version="0.1.0",
    docs_url=None,
    redoc_url=None,
    openapi_url=None if settings.is_production else "/openapi.json",
    lifespan=lifespan,
)

if not settings.is_production:
    configure_openapi(app)
    add_scalar_reference(
        app,
        route="/scalar",
        title="AI Engine — RAG & Evaluación",
    )


# OWASP A10: never leak stack traces or internal details to the caller.
@app.exception_handler(Exception)
async def unhandled_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    logger.exception("Unhandled error on %s %s", request.method, request.url.path)
    return JSONResponse(status_code=500, content={"title": "Internal Server Error", "status": 500})


app.include_router(health.router)

# Every business router goes here, protected by the internal API key:
protected = [Depends(require_internal_api_key)]
app.include_router(cvs.router, dependencies=protected)
app.include_router(evaluations.router, dependencies=protected)
app.include_router(rag.router, dependencies=protected)
app.include_router(role_profiles.router, dependencies=protected)
app.include_router(readiness.router, dependencies=protected)
app.include_router(assistant.router, dependencies=protected)
