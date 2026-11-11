import logging

from fastapi import Depends, FastAPI, Request
from fastapi.responses import JSONResponse

from ai_engine.api.routers import health
from ai_engine.core.config import get_settings
from ai_engine.core.security import require_internal_api_key

logger = logging.getLogger("ai_engine")
settings = get_settings()

app = FastAPI(
    title="AI Talent Scout - AI Engine",
    description="Ingesta y anonimización de CVs, extracción, RAG y agentes de evaluación.",
    version="0.1.0",
    # OWASP A02: no public API docs in production.
    docs_url=None if settings.is_production else "/docs",
    redoc_url=None,
    openapi_url=None if settings.is_production else "/openapi.json",
)


# OWASP A10: never leak stack traces or internal details to the caller.
@app.exception_handler(Exception)
async def unhandled_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    logger.exception("Unhandled error on %s %s", request.method, request.url.path)
    return JSONResponse(status_code=500, content={"title": "Internal Server Error", "status": 500})


app.include_router(health.router)

# Every business router goes here, protected by the internal API key:
protected = [Depends(require_internal_api_key)]
# app.include_router(evaluations.router, dependencies=protected)
