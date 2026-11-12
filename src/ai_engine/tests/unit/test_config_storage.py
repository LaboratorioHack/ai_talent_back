from ai_engine.core.config import Settings


def test_auto_prefers_r2_when_keys_present() -> None:
    settings = Settings(
        storage_backend="auto",
        r2_access_key_id="key",
        r2_secret_access_key="secret",
        r2_bucket_name="cvs",
        r2_account_id="acct",
        r2_endpoint_url="",
    )
    assert settings.uses_r2_storage is True
    assert settings.r2_endpoint == "https://acct.r2.cloudflarestorage.com"


def test_supabase_backend_ignores_r2_keys() -> None:
    settings = Settings(
        storage_backend="supabase",
        r2_access_key_id="key",
        r2_secret_access_key="secret",
    )
    assert settings.uses_r2_storage is False
