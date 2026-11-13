from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    environment: str = "development"

    engine_api_key: str = ""

    anthropic_api_key: str = ""
    anthropic_model: str = "claude-sonnet-5-5"
    anthropic_vision_model: str = ""

    # Embeddings: Nvidia (primary) + Voyage (fallback), both via OpenRouter
    openrouter_api_key: str = ""
    openrouter_base_url: str = "https://openrouter.ai/api/v1"
    embeddings_dimensions: int = 1024
    embeddings_model: str = "nvidia/nemotron-3.5-embed-1b:free"
    embeddings_model_fallback: str = "mongodb/voyage-4-lite"

    # Jev (TypeSafe System One) via OpenRouter Decisions API — same OPENROUTER_API_KEY
    jev_model_id: str = "typesafe/jev-1.14"
    # CVs evaluated at the same time when ranking a vacancy (each one is a Claude call).
    rank_concurrency: int = 3
    jev_decisions_url: str = "https://openrouter.ai/api/alpha/decisions"
    jev_noul_yes: float = 0.80
    jev_noul_no: float = 0.20

    supabase_url: str = ""
    supabase_service_role_key: str = ""
    database_url: str = ""

    # CV files: `supabase` | `r2` | `auto` (R2 if R2_ACCESS_KEY_ID is set, else Supabase)
    storage_backend: str = "auto"
    r2_account_id: str = ""
    r2_access_key_id: str = ""
    r2_secret_access_key: str = ""
    r2_bucket_name: str = "cvs"
    r2_endpoint_url: str = ""
    r2_public_base_url: str = ""

    max_upload_bytes: int = 10 * 1024 * 1024
    extractor_version: str = "v1.1.0"
    prompt_version: str = "eval-v1"

    @property
    def is_production(self) -> bool:
        return self.environment.lower() == "production"

    @property
    def vision_model(self) -> str:
        return self.anthropic_vision_model or self.anthropic_model

    @property
    def r2_endpoint(self) -> str:
        if self.r2_endpoint_url:
            return self.r2_endpoint_url.rstrip("/")
        if self.r2_account_id:
            return f"https://{self.r2_account_id}.r2.cloudflarestorage.com"
        return ""

    @property
    def uses_r2_storage(self) -> bool:
        backend = self.storage_backend.lower()
        if backend == "r2":
            return True
        if backend == "supabase":
            return False
        return bool(self.r2_access_key_id and self.r2_secret_access_key and self.r2_bucket_name)


@lru_cache
def get_settings() -> Settings:
    return Settings()
