from unittest.mock import MagicMock

import pytest

from ai_engine.core.config import Settings
from ai_engine.persistence import storage


def test_download_cv_from_r2(monkeypatch: pytest.MonkeyPatch) -> None:
    storage.reset_r2_client_cache()
    settings = Settings(
        storage_backend="r2",
        r2_access_key_id="access",
        r2_secret_access_key="secret",
        r2_account_id="abc123",
        r2_bucket_name="cvs",
    )
    monkeypatch.setattr(storage, "get_settings", lambda: settings)

    body = MagicMock()
    body.read.return_value = b"%PDF-fake"
    s3 = MagicMock()
    s3.get_object.return_value = {"Body": body}
    monkeypatch.setattr(storage.boto3, "client", lambda *_args, **_kwargs: s3)

    data = storage.download_cv("candidates/u1/resume.pdf")
    assert data == b"%PDF-fake"
    s3.get_object.assert_called_once_with(Bucket="cvs", Key="candidates/u1/resume.pdf")


def test_storage_configured_r2() -> None:
    settings = Settings(
        storage_backend="r2",
        r2_access_key_id="access",
        r2_secret_access_key="secret",
        r2_account_id="abc123",
        r2_bucket_name="cvs",
    )
    assert storage.storage_configured(settings) is True
    assert (
        storage.storage_configured(
            Settings(storage_backend="supabase", supabase_url="", supabase_service_role_key="")
        )
        is False
    )
    assert storage.r2_configured(settings) is True


def test_download_cv_supabase_requires_credentials(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        storage,
        "get_settings",
        lambda: Settings(storage_backend="supabase", supabase_url="", supabase_service_role_key=""),
    )
    with pytest.raises(RuntimeError, match="SUPABASE"):
        storage.download_cv("x.pdf")
