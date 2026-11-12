"""Download and upload CV binaries from Cloudflare R2 (preferred) or Supabase Storage.

Raw CV files live in object storage. Postgres/pgvector only stores file_key metadata,
anonymized chunks, and embeddings — never the binary.
"""

from __future__ import annotations

import asyncio
from functools import lru_cache

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


def upload_cv(
    file_key: str,
    content: bytes,
    content_type: str = "application/pdf",
    settings: Settings | None = None,
) -> str:
    resolved = settings or get_settings()
    key = file_key.lstrip("/")
    if resolved.uses_r2_storage:
        _upload_r2(key, content, content_type, resolved)
    else:
        _upload_supabase(key, content, content_type, resolved)
    return key


async def upload_cv_async(
    file_key: str,
    content: bytes,
    content_type: str = "application/pdf",
    settings: Settings | None = None,
) -> str:
    return await asyncio.to_thread(upload_cv, file_key, content, content_type, settings)


async def download_cv_async(file_key: str) -> bytes:
    return await asyncio.to_thread(download_cv, file_key)


def storage_configured(settings: Settings | None = None) -> bool:
    resolved = settings or get_settings()
    if resolved.uses_r2_storage:
        return r2_configured(resolved)
    return bool(resolved.supabase_url and resolved.supabase_service_role_key)


def r2_configured(settings: Settings | None = None) -> bool:
    resolved = settings or get_settings()
    return bool(
        resolved.r2_access_key_id
        and resolved.r2_secret_access_key
        and resolved.r2_endpoint
        and resolved.r2_bucket_name
    )


def _download_supabase(file_key: str, settings: Settings) -> bytes:
    if not settings.supabase_url or not settings.supabase_service_role_key:
        msg = "SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required for Supabase storage"
        raise RuntimeError(msg)
    client: Client = create_client(settings.supabase_url, settings.supabase_service_role_key)
    return client.storage.from_("cvs").download(file_key)


@lru_cache(maxsize=4)
def _r2_client_from_keys(access_key: str, secret_key: str, endpoint: str) -> BaseClient:
    return boto3.client(
        "s3",
        endpoint_url=endpoint,
        aws_access_key_id=access_key,
        aws_secret_access_key=secret_key,
        region_name="auto",
        config=Config(
            signature_version="s3v4",
            s3={"addressing_style": "path"},
            max_pool_connections=20,
        ),
    )


def reset_r2_client_cache() -> None:
    _r2_client_from_keys.cache_clear()


def _r2_client(settings: Settings) -> BaseClient:
    if not settings.r2_access_key_id or not settings.r2_secret_access_key:
        msg = "R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY are required"
        raise RuntimeError(msg)
    endpoint = settings.r2_endpoint
    if not endpoint:
        msg = "R2_ACCOUNT_ID or R2_ENDPOINT_URL is required"
        raise RuntimeError(msg)
    return _r2_client_from_keys(settings.r2_access_key_id, settings.r2_secret_access_key, endpoint)


def _download_r2(file_key: str, settings: Settings) -> bytes:
    client = _r2_client(settings)
    response = client.get_object(Bucket=settings.r2_bucket_name, Key=file_key.lstrip("/"))
    return response["Body"].read()


def _upload_r2(file_key: str, content: bytes, content_type: str, settings: Settings) -> None:
    client = _r2_client(settings)
    client.put_object(
        Bucket=settings.r2_bucket_name,
        Key=file_key,
        Body=content,
        ContentType=content_type,
    )


def _upload_supabase(file_key: str, content: bytes, content_type: str, settings: Settings) -> None:
    if not settings.supabase_url or not settings.supabase_service_role_key:
        msg = "SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required for Supabase storage"
        raise RuntimeError(msg)
    client: Client = create_client(settings.supabase_url, settings.supabase_service_role_key)
    client.storage.from_("cvs").upload(
        path=file_key,
        file=content,
        file_options={"content-type": content_type, "upsert": "true"},
    )
