"""Seed app.candidates + app.resumes from the Kaggle resume dataset + randomuser.me.

Each synthetic user is paired 1:1 with a sampled CV (PDF preferred, Resume_str fallback).
"""

from __future__ import annotations

import argparse
import asyncio
import csv
import hashlib
import os
import random
import sys
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import httpx

from ai_engine.core.config import get_settings
from ai_engine.ingestion.validation import InvalidCvError
from ai_engine.persistence import repositories
from ai_engine.persistence.db import close_pool
from ai_engine.persistence.storage import r2_configured, upload_cv_async
from ai_engine.services.ingest_service import IngestService

KAGGLE_DATASET = "snehaanbhawal/resume-dataset"
RANDOMUSER_URL = "https://randomuser.me/api/"
DEFAULT_COMPANY_NAME = "Demo Company"
DEFAULT_SEED = "ai-talent-scout"
DEFAULT_LIMIT = 25
DEFAULT_CONCURRENCY = 10
_PDF_MAGIC = b"%PDF-"


@dataclass(frozen=True)
class ResumeSample:
    kaggle_id: str
    category: str
    resume_str: str
    pdf_path: Path | None


@dataclass(frozen=True)
class SyntheticUser:
    first: str
    last: str
    email: str
    name: str


@dataclass(frozen=True)
class SeedOutcome:
    kaggle_id: str
    status: str
    resume_id: uuid.UUID | None = None
    detail: str = ""


def load_env_file(path: Path) -> None:
    if not path.is_file():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        if not key or key in os.environ:
            continue
        os.environ[key] = value.strip().strip('"').strip("'")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[4]


def resolve_dataset_dir(dataset_dir: Path | str | None) -> Path:
    if dataset_dir:
        path = Path(dataset_dir)
        if not path.is_dir():
            msg = f"Dataset directory does not exist: {path}"
            raise FileNotFoundError(msg)
        return path
    import kagglehub

    downloaded = kagglehub.dataset_download(KAGGLE_DATASET)
    return Path(downloaded)


def find_resume_csv(dataset_root: Path) -> Path:
    matches = sorted(dataset_root.rglob("Resume.csv"))
    if not matches:
        msg = f"Resume.csv not found under {dataset_root}"
        raise FileNotFoundError(msg)
    return matches[0]


def resolve_pdf_path(dataset_root: Path, category: str, kaggle_id: str) -> Path | None:
    filename = f"{kaggle_id}.pdf"
    candidates = [
        dataset_root / "data" / "data" / category / filename,
        dataset_root / "data" / category / filename,
        dataset_root / category / filename,
    ]
    for path in candidates:
        if path.is_file():
            return path
    matches = list(dataset_root.glob(f"**/{category}/{filename}"))
    return matches[0] if matches else None


def load_resume_samples(dataset_root: Path, *, category: str | None = None) -> list[ResumeSample]:
    csv_path = find_resume_csv(dataset_root)
    samples: list[ResumeSample] = []
    with csv_path.open(newline="", encoding="utf-8", errors="replace") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            kaggle_id = str(row.get("ID") or "").strip()
            cat = str(row.get("Category") or "").strip()
            resume_str = str(row.get("Resume_str") or "")
            if not kaggle_id or not cat:
                continue
            if category and cat.upper() != category.upper():
                continue
            samples.append(
                ResumeSample(
                    kaggle_id=kaggle_id,
                    category=cat,
                    resume_str=resume_str,
                    pdf_path=resolve_pdf_path(dataset_root, cat, kaggle_id),
                )
            )
    return samples


def stratified_sample(
    rows: list[ResumeSample],
    limit: int,
    rng: random.Random,
) -> list[ResumeSample]:
    if limit <= 0 or not rows:
        return []
    if limit >= len(rows):
        return list(rows)

    by_cat: dict[str, list[ResumeSample]] = {}
    for row in rows:
        by_cat.setdefault(row.category, []).append(row)
    for bucket in by_cat.values():
        rng.shuffle(bucket)

    sampled: list[ResumeSample] = []
    categories = sorted(by_cat)
    index = 0
    while len(sampled) < limit and by_cat:
        cat = categories[index % len(categories)]
        index += 1
        bucket = by_cat[cat]
        sampled.append(bucket.pop())
        if not bucket:
            del by_cat[cat]
            categories = sorted(by_cat)
            index = 0
    return sampled


def format_name(first: str, last: str) -> str:
    return f"{first} {last}".strip()


def parse_randomuser_results(payload: dict[str, Any]) -> list[SyntheticUser]:
    users: list[SyntheticUser] = []
    for item in payload.get("results") or []:
        if not isinstance(item, dict):
            continue
        name = item.get("name") if isinstance(item.get("name"), dict) else {}
        first = str(name.get("first") or "").strip()
        last = str(name.get("last") or "").strip()
        email = str(item.get("email") or "").strip()
        if not first and not last:
            continue
        users.append(
            SyntheticUser(
                first=first,
                last=last,
                email=email,
                name=format_name(first, last) or email,
            )
        )
    return users


def unique_email(email: str, kaggle_id: str, used: set[str]) -> str:
    candidate = email.strip().lower()
    if candidate and candidate not in used:
        used.add(candidate)
        return candidate
    local = candidate.split("@", 1)[0] if "@" in candidate else "user"
    local = local or "user"
    fallback = f"{local}.{kaggle_id}@example.com".lower()
    suffix = 1
    while fallback in used:
        fallback = f"{local}.{kaggle_id}.{suffix}@example.com".lower()
        suffix += 1
    used.add(fallback)
    return fallback


def pair_users(
    users: list[SyntheticUser],
    samples: list[ResumeSample],
) -> list[tuple[SyntheticUser, ResumeSample]]:
    used: set[str] = set()
    pairs: list[tuple[SyntheticUser, ResumeSample]] = []
    for index, sample in enumerate(samples):
        if index < len(users):
            source = users[index]
            first, last, name = source.first, source.last, source.name
            raw_email = source.email
        else:
            first, last = "Candidate", sample.kaggle_id
            name = format_name(first, last)
            raw_email = ""
        email = unique_email(raw_email, sample.kaggle_id, used)
        if not name:
            name = format_name(first, last) or email
        pairs.append(
            (
                SyntheticUser(first=first, last=last, email=email, name=name),
                sample,
            )
        )
    return pairs


async def fetch_random_users(
    count: int,
    seed: str,
    client: httpx.AsyncClient | None = None,
) -> list[SyntheticUser]:
    if count <= 0:
        return []
    owns_client = client is None
    http = client or httpx.AsyncClient(timeout=30.0)
    try:
        response = await http.get(
            RANDOMUSER_URL,
            params={"results": count, "seed": seed, "inc": "name,email,nat"},
        )
        response.raise_for_status()
        payload = response.json()
        if not isinstance(payload, dict):
            msg = "randomuser.me returned a non-object payload"
            raise RuntimeError(msg)
        return parse_randomuser_results(payload)
    finally:
        if owns_client:
            await http.aclose()


def load_sample_content(sample: ResumeSample) -> tuple[bytes, str, str]:
    """Return (bytes, content_type, source) where source is 'pdf' or 'text'."""
    if sample.pdf_path and sample.pdf_path.is_file():
        content = sample.pdf_path.read_bytes()
        if content.startswith(_PDF_MAGIC):
            return content, "application/pdf", "pdf"
    text = sample.resume_str.strip()
    if not text:
        msg = f"No PDF or Resume_str for Kaggle ID {sample.kaggle_id}"
        raise FileNotFoundError(msg)
    return text.encode("utf-8"), "text/plain", "text"


def resume_object_key(company_id: uuid.UUID, sample: ResumeSample, content_type: str) -> str:
    suffix = "pdf" if content_type == "application/pdf" else "txt"
    return f"cvs/{company_id}/{sample.category}/{sample.kaggle_id}.{suffix}"


def _require_r2(skip_storage: bool) -> None:
    if skip_storage:
        return
    if not r2_configured(get_settings()):
        msg = (
            "R2 credentials are required so CV binaries stay in object storage "
            "(not the vector DB). Set R2_ACCESS_KEY_ID, R2_SECRET_ACCESS_KEY, "
            "R2_BUCKET_NAME and R2_ACCOUNT_ID / R2_ENDPOINT_URL, or pass --skip-storage."
        )
        raise SystemExit(msg)


async def seed_one(
    *,
    sample: ResumeSample,
    content: bytes,
    content_type: str,
    source: str,
    file_key: str,
    resume_id: uuid.UUID,
    service: IngestService,
    skip_storage: bool,
) -> SeedOutcome:
    try:
        upload_task: asyncio.Task[str] | None = None
        if not skip_storage:
            upload_task = asyncio.create_task(
                upload_cv_async(file_key, content, content_type=content_type)
            )

        async def _ingest() -> None:
            if source == "pdf":
                try:
                    await service.ingest_resume_bytes(resume_id, content)
                    return
                except (InvalidCvError, ValueError, RuntimeError):
                    if not sample.resume_str.strip():
                        raise
                    await service.ingest_resume_text(resume_id, sample.resume_str)
                    return
            await service.ingest_resume_text(resume_id, sample.resume_str)

        ingest_task = asyncio.create_task(_ingest())
        try:
            await ingest_task
        finally:
            if upload_task is not None:
                await upload_task
    except (InvalidCvError, ValueError, RuntimeError, OSError) as exc:
        return SeedOutcome(
            kaggle_id=sample.kaggle_id,
            status="failed",
            resume_id=resume_id,
            detail=str(exc)[:400],
        )
    return SeedOutcome(kaggle_id=sample.kaggle_id, status="seeded", resume_id=resume_id)


async def seed(
    *,
    limit: int = DEFAULT_LIMIT,
    category: str | None = None,
    company_name: str = DEFAULT_COMPANY_NAME,
    company_id: uuid.UUID | None = None,
    concurrency: int = DEFAULT_CONCURRENCY,
    dataset_dir: Path | str | None = None,
    skip_storage: bool = False,
    force: bool = False,
    seed: str = DEFAULT_SEED,
    ingest_service: IngestService | None = None,
    http_client: httpx.AsyncClient | None = None,
) -> list[SeedOutcome]:
    dataset_root = resolve_dataset_dir(dataset_dir)
    samples = load_resume_samples(dataset_root, category=category)
    if not samples:
        msg = "No resumes found in the dataset for the given filters"
        raise SystemExit(msg)

    rng = random.Random(seed)
    selected = stratified_sample(samples, limit, rng)
    users = await fetch_random_users(len(selected), seed, client=http_client)
    pairs = pair_users(users, selected)

    _require_r2(skip_storage)
    resolved_company = await repositories.get_or_create_company(company_name, company_id)

    prepared_rows: list[dict[str, Any]] = []
    work: list[tuple[SyntheticUser, ResumeSample, bytes, str, str, str, str]] = []
    for user, sample in pairs:
        content, content_type, source = load_sample_content(sample)
        file_hash = hashlib.sha256(content).hexdigest()
        file_key = resume_object_key(resolved_company, sample, content_type)
        prepared_rows.append(
            {
                "name": user.name,
                "contact_email": user.email,
                "file_key": file_key,
                "file_hash": file_hash,
                "kaggle_id": sample.kaggle_id,
            }
        )
        work.append((user, sample, content, content_type, source, file_key, file_hash))

    prepared = await repositories.bulk_prepare_seed_rows(
        resolved_company, prepared_rows, force=force
    )
    prepared_by_hash = {row["file_hash"]: row for row in prepared}

    service = ingest_service or IngestService()
    semaphore = asyncio.Semaphore(max(1, concurrency))

    async def _run(
        user: SyntheticUser,
        sample: ResumeSample,
        content: bytes,
        content_type: str,
        source: str,
        file_key: str,
        file_hash: str,
    ) -> SeedOutcome:
        row = prepared_by_hash[file_hash]
        if row.get("skipped"):
            return SeedOutcome(
                kaggle_id=sample.kaggle_id,
                status="skipped",
                resume_id=row["resume_id"],
                detail="already PROCESSED",
            )
        async with semaphore:
            print(f"  -> {sample.category}/{sample.kaggle_id}  ({user.email})")
            return await seed_one(
                sample=sample,
                content=content,
                content_type=content_type,
                source=source,
                file_key=file_key,
                resume_id=row["resume_id"],
                service=service,
                skip_storage=skip_storage,
            )

    outcomes = await asyncio.gather(
        *(
            _run(user, sample, content, content_type, source, file_key, file_hash)
            for user, sample, content, content_type, source, file_key, file_hash in work
        )
    )
    return list(outcomes)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Pool Kaggle resumes with randomuser.me candidates and run RAG ingest."
    )
    parser.add_argument("--limit", type=int, default=DEFAULT_LIMIT)
    parser.add_argument("--category", type=str, default=None)
    parser.add_argument("--company-name", type=str, default=DEFAULT_COMPANY_NAME)
    parser.add_argument("--company-id", type=str, default=None)
    parser.add_argument("--concurrency", type=int, default=DEFAULT_CONCURRENCY)
    parser.add_argument("--dataset-dir", type=str, default=None)
    parser.add_argument("--skip-storage", action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--seed", type=str, default=DEFAULT_SEED)
    return parser.parse_args(argv)


def _print_summary(outcomes: list[SeedOutcome]) -> None:
    counts: dict[str, int] = {}
    for item in outcomes:
        counts[item.status] = counts.get(item.status, 0) + 1
        if item.status == "failed":
            print(f"  x {item.kaggle_id}: {item.detail}", file=sys.stderr)
    seeded = counts.get("seeded", 0)
    skipped = counts.get("skipped", 0)
    failed = counts.get("failed", 0)
    print(f"Seeded {seeded}/{len(outcomes)} resumes ({skipped} skipped, {failed} failed)")


async def _seed_and_close(**kwargs: Any) -> list[SeedOutcome]:
    try:
        return await seed(**kwargs)
    finally:
        await close_pool()


def main(argv: list[str] | None = None) -> None:
    load_env_file(repo_root() / ".env.development")
    args = parse_args(argv)
    company_id = uuid.UUID(args.company_id) if args.company_id else None
    outcomes = asyncio.run(
        _seed_and_close(
            limit=args.limit,
            category=args.category,
            company_name=args.company_name,
            company_id=company_id,
            concurrency=args.concurrency,
            dataset_dir=args.dataset_dir,
            skip_storage=args.skip_storage,
            force=args.force,
            seed=args.seed,
        )
    )
    _print_summary(outcomes)
    if any(item.status == "failed" for item in outcomes):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
