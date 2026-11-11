"""Postgres access for the private `app` schema."""

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

import psycopg
from pgvector.psycopg import register_vector
from psycopg.rows import dict_row

from ai_engine.core.config import get_settings


@asynccontextmanager
async def connection() -> AsyncIterator[psycopg.AsyncConnection]:
    settings = get_settings()
    if not settings.database_url:
        msg = "DATABASE_URL is not configured"
        raise RuntimeError(msg)
    async with await psycopg.AsyncConnection.connect(
        settings.database_url,
        row_factory=dict_row,
    ) as conn:
        await register_vector(conn)
        yield conn
