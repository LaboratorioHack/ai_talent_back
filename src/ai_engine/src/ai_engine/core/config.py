from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    # Local dev: run from src/ai_engine -> reads ai_talent_back/.env.development (shared with
    # the .NET API); a local src/ai_engine/.env overrides it. Missing files are ignored, and real
    # environment variables (Docker/production) always take precedence over both.
    model_config = SettingsConfigDict(env_file=("../../.env.development", ".env"), extra="ignore")

    environment: str = "development"

    # Shared secret with the .NET API (header X-Internal-Api-Key).
    engine_api_key: str = ""

    anthropic_api_key: str = ""
    anthropic_model: str = "claude-sonnet-5-5"

    embeddings_provider: str = "voyage"
    voyage_api_key: str = ""

    supabase_url: str = ""
    supabase_service_role_key: str = ""
    database_url: str = ""

    max_upload_bytes: int = 10 * 1024 * 1024

    @property
    def is_production(self) -> bool:
        return self.environment.lower() == "production"


@lru_cache
def get_settings() -> Settings:
    return Settings()
