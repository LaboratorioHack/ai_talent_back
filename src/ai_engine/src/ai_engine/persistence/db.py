"""Postgres access for the private `app` schema via a pooled connection."""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

import psycopg
from pgvector.psycopg import register_vector_async
from psycopg.rows import dict_row
from psycopg_pool import AsyncConnectionPool

from ai_engine.core.config import get_settings

_pool: AsyncConnectionPool | None = None


async def _configure(conn: psycopg.AsyncConnection) -> None:
    await register_vector_async(conn)


async def get_pool() -> AsyncConnectionPool:
    global _pool
    if _pool is None or _pool.closed:
        settings = get_settings()
        if not settings.database_url:
            msg = "DATABASE_URL is not configured"
            raise RuntimeError(msg)
        _pool = AsyncConnectionPool(
            conninfo=settings.database_url,
            min_size=1,
            max_size=12,
            kwargs={
                "row_factory": dict_row,
                "prepare_threshold": None,  # PgBouncer / Supavisor transaction mode
            },
            configure=_configure,
            open=False,
        )
        await _pool.open()
    return _pool


async def close_pool() -> None:
    global _pool
    if _pool is not None and not _pool.closed:
        await _pool.close()
    _pool = None


@asynccontextmanager
async def connection() -> AsyncIterator[psycopg.AsyncConnection]:
    pool = await get_pool()
    async with pool.connection() as conn:
        yield conn
