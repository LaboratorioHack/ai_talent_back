"""Download CV binaries from Supabase Storage or Cloudflare R2 (S3-compatible)."""

import boto3
from botocore.client import BaseClient
from botocore.config import Config
from supabase import Client, create_client

from ai_engine.core.config import Settings, get_settings


def download_cv(file_key: str) -> bytes:
    settings = get_settings()
    if settings.uses_r2_storage:
        return _download_r2(file_key, settings)
    return _download_supabase(file_key, settings)


def _download_supabase(file_key: str, settings: Settings) -> bytes:
    if not settings.supabase_url or not settings.supabase_service_role_key:
        msg = "SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required for Supabase storage"
        raise RuntimeError(msg)
    client: Client = create_client(settings.supabase_url, settings.supabase_service_role_key)
    return client.storage.from_("cvs").download(file_key)


def _r2_client(settings: Settings) -> BaseClient:
    if not settings.r2_access_key_id or not settings.r2_secret_access_key:
        msg = "R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY are required"
        raise RuntimeError(msg)
    endpoint = settings.r2_endpoint
    if not endpoint:
        msg = "R2_ACCOUNT_ID or R2_ENDPOINT_URL is required"
        raise RuntimeError(msg)
    return boto3.client(
        "s3",
        endpoint_url=endpoint,
        aws_access_key_id=settings.r2_access_key_id,
        aws_secret_access_key=settings.r2_secret_access_key,
        region_name="auto",
        config=Config(signature_version="s3v4", s3={"addressing_style": "path"}),
    )


def _download_r2(file_key: str, settings: Settings) -> bytes:
    client = _r2_client(settings)
    response = client.get_object(Bucket=settings.r2_bucket_name, Key=file_key.lstrip("/"))
    return response["Body"].read()
