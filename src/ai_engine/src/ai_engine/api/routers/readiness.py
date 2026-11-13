"""Readiness: what the engine needs to do real work, as yes/no flags (never the values).

Protected by the internal API key like the business routers, so a 401 here also tells the
caller that its key does not match. Used by the API's ``/api/health/ai`` for operators.
"""

from __future__ import annotations

import logging

from fastapi import APIRouter

from ai_engine.core.config import get_settings
from ai_engine.core.llm import LlmClient
from ai_engine.persistence.db import connection

logger = logging.getLogger(__name__)
router = APIRouter(tags=["health"])


async def _database() -> str:
    try:
        async with connection() as conn:
            await conn.execute("select 1")
        return "ok"
    except Exception as exc:  # noqa: BLE001 — reported as a flag, logged for operators
        logger.warning("Readiness: database check failed: %s", exc)
        return type(exc).__name__


async def _llm() -> str:
    try:
        answer = await LlmClient().complete_text("Answer with one word.", "Say ok.", max_tokens=5)
        return "ok" if answer.strip() else "empty"
    except Exception as exc:  # noqa: BLE001
        logger.warning("Readiness: LLM check failed: %s", exc)
        return type(exc).__name__


@router.get("/health/ready")
async def ready() -> dict[str, object]:
    settings = get_settings()
    checks: dict[str, object] = {
        "anthropic_key": bool(settings.anthropic_api_key),
        "openrouter_key": bool(settings.openrouter_api_key),
        "model": settings.anthropic_model,
        "database": await _database(),
        "llm": await _llm(),
    }
    checks["ready"] = checks["database"] == "ok" and checks["llm"] == "ok"
    return checks
