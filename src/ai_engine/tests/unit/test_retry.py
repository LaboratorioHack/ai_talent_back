from ai_engine.core.retry import bounded_retry_delay, retry_after_from_headers


def test_retry_after_seconds_header() -> None:
    assert retry_after_from_headers({"Retry-After": "2"}) == 2.0


def test_retry_after_ms_header() -> None:
    assert retry_after_from_headers({"retry-after-ms": "1500"}) == 1.5


def test_retry_after_missing() -> None:
    assert retry_after_from_headers({}) is None
    assert retry_after_from_headers(None) is None


def test_bounded_retry_delay_without_header() -> None:
    assert bounded_retry_delay(0, floors=(5.0, 10.0, 15.0), header=None) == 5.0
