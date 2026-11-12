"""Isolated retry helpers for Anthropic rate limits and OpenRouter embeddings."""

from __future__ import annotations

import asyncio
import random
from collections.abc import Awaitable, Callable, Mapping, Sequence


def full_jitter(base: float, attempt: int, cap: float = 30.0) -> float:
    return min(cap, random.random() * (base * (2**attempt)))


def retry_after_from_headers(headers: Mapping[str, str] | None) -> float | None:
    if not headers:
        return None
    lowered = {str(key).lower(): str(value) for key, value in headers.items()}
    raw = lowered.get("retry-after-ms")
    if raw:
        try:
            return max(0.0, float(raw) / 1000.0)
        except ValueError:
            pass
    raw = lowered.get("retry-after")
    if raw:
        try:
            return max(0.0, float(raw))
        except ValueError:
            return None
    return None


def headers_from_exc(exc: BaseException) -> Mapping[str, str] | None:
    response = getattr(exc, "response", None)
    if response is None:
        return None
    return getattr(response, "headers", None)


def bounded_retry_delay(
    attempt: int,
    *,
    floors: Sequence[float],
    header: float | None,
    cap: float = 60.0,
) -> float:
    """Sleep before the next attempt. ``attempt`` is 0-based (first failure = 0)."""
    if not floors:
        return min(cap, header if header is not None else 0.0)
    index = min(max(attempt, 0), len(floors) - 1)
    floor = floors[index]
    header_val = header if header is not None else 0.0
    return min(cap, max(floor, header_val))


async def call_with_backoff[T](
    factory: Callable[[], Awaitable[T]],
    *,
    is_retryable: Callable[[BaseException], bool],
    max_attempts: int = 5,
    base: float = 1.5,
    retry_after: Callable[[BaseException], float | None] | None = None,
    delay_floors: Sequence[float] | None = None,
    delay_cap: float = 60.0,
    on_retry: Callable[[int, int, BaseException, float], None] | None = None,
) -> T:
    last_exc: BaseException | None = None
    attempts = max(1, max_attempts)
    for attempt in range(attempts):
        try:
            return await factory()
        except BaseException as exc:
            if not is_retryable(exc) or attempt >= attempts - 1:
                raise
            last_exc = exc
            header_delay = retry_after(exc) if retry_after else None
            if delay_floors is not None:
                delay = bounded_retry_delay(
                    attempt, floors=delay_floors, header=header_delay, cap=delay_cap
                )
            else:
                delay = header_delay if header_delay is not None else full_jitter(base, attempt)
            if on_retry is not None:
                on_retry(attempt + 2, attempts, exc, delay)
            await asyncio.sleep(delay)
    assert last_exc is not None
    raise last_exc
