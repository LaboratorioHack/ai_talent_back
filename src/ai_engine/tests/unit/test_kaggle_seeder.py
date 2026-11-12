import csv
import random
import uuid
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

from ai_engine.core.config import Settings
from ai_engine.persistence import storage
from ai_engine.seeding import kaggle_seeder
from ai_engine.seeding.kaggle_seeder import (
    ResumeSample,
    fetch_random_users,
    find_resume_csv,
    load_resume_samples,
    load_sample_content,
    pair_users,
    parse_randomuser_results,
    resolve_pdf_path,
    seed_one,
    stratified_sample,
    unique_email,
)


def _write_csv(path: Path, rows: list[dict[str, str]]) -> None:
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["ID", "Resume_str", "Resume_html", "Category"])
        writer.writeheader()
        writer.writerows(rows)


def test_parse_randomuser_results_formats_name_and_email() -> None:
    users = parse_randomuser_results(
        {
            "results": [
                {
                    "name": {"title": "Ms", "first": "Ada", "last": "Lovelace"},
                    "email": "ada.lovelace@example.com",
                },
                {"name": {"first": "", "last": ""}, "email": "skip@example.com"},
            ]
        }
    )
    assert len(users) == 1
    assert users[0].name == "Ada Lovelace"
    assert users[0].email == "ada.lovelace@example.com"


def test_unique_email_falls_back_on_collision() -> None:
    used: set[str] = set()
    first = unique_email("Ada.Lovelace@example.com", "1001", used)
    second = unique_email("ada.lovelace@example.com", "1002", used)
    assert first == "ada.lovelace@example.com"
    assert second == "ada.lovelace.1002@example.com"
    assert first != second


def test_pair_users_is_one_to_one_and_unique() -> None:
    users = parse_randomuser_results(
        {
            "results": [
                {"name": {"first": "Ada", "last": "Lovelace"}, "email": "ada@example.com"},
                {"name": {"first": "Alan", "last": "Turing"}, "email": "ada@example.com"},
            ]
        }
    )
    samples = [
        ResumeSample("1", "ENGINEERING", "cv one", None),
        ResumeSample("2", "HR", "cv two", None),
        ResumeSample("3", "SALES", "cv three", None),
    ]
    pairs = pair_users(users, samples)
    assert len(pairs) == 3
    emails = [user.email for user, _ in pairs]
    assert len(set(emails)) == 3
    assert pairs[0][0].name == "Ada Lovelace"
    assert pairs[2][0].name == "Candidate 3"
    assert pairs[2][1].kaggle_id == "3"


def test_find_resume_csv_and_pdf_layout(tmp_path: Path) -> None:
    csv_path = tmp_path / "Resume.csv"
    pdf_dir = tmp_path / "data" / "data" / "INFORMATION-TECHNOLOGY"
    pdf_dir.mkdir(parents=True)
    pdf_path = pdf_dir / "16866401.pdf"
    pdf_path.write_bytes(b"%PDF-1.4 fake")
    _write_csv(
        csv_path,
        [
            {
                "ID": "16866401",
                "Resume_str": "Python engineer",
                "Resume_html": "",
                "Category": "INFORMATION-TECHNOLOGY",
            }
        ],
    )

    assert find_resume_csv(tmp_path) == csv_path
    assert resolve_pdf_path(tmp_path, "INFORMATION-TECHNOLOGY", "16866401") == pdf_path

    samples = load_resume_samples(tmp_path)
    assert len(samples) == 1
    assert samples[0].pdf_path == pdf_path
    assert samples[0].resume_str == "Python engineer"


def test_load_resume_samples_filters_category(tmp_path: Path) -> None:
    _write_csv(
        tmp_path / "Resume.csv",
        [
            {"ID": "1", "Resume_str": "a", "Resume_html": "", "Category": "HR"},
            {"ID": "2", "Resume_str": "b", "Resume_html": "", "Category": "ENGINEERING"},
        ],
    )
    samples = load_resume_samples(tmp_path, category="engineering")
    assert [s.kaggle_id for s in samples] == ["2"]


def test_stratified_sample_covers_categories() -> None:
    rows = [
        ResumeSample(str(i), cat, "text", None)
        for cat in ("HR", "ENGINEERING", "SALES")
        for i in range({"HR": 10, "ENGINEERING": 10, "SALES": 10}[cat])
    ]
    sampled = stratified_sample(rows, limit=6, rng=random.Random(0))
    assert len(sampled) == 6
    assert {row.category for row in sampled} == {"HR", "ENGINEERING", "SALES"}


@pytest.mark.asyncio
async def test_fetch_random_users_uses_seed_and_results() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.params["results"] == "2"
        assert request.url.params["seed"] == "ai-talent-scout"
        return httpx.Response(
            200,
            json={
                "results": [
                    {"name": {"first": "Ada", "last": "Lovelace"}, "email": "ada@example.com"},
                    {"name": {"first": "Alan", "last": "Turing"}, "email": "alan@example.com"},
                ]
            },
        )

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as client:
        users = await fetch_random_users(2, "ai-talent-scout", client=client)
    assert [user.name for user in users] == ["Ada Lovelace", "Alan Turing"]


@pytest.mark.asyncio
async def test_seed_one_ingests_pdf(tmp_path: Path) -> None:
    pdf = tmp_path / "1.pdf"
    pdf.write_bytes(b"%PDF-1.4 body")
    sample = ResumeSample("1", "HR", "plain", pdf)
    resume_id = uuid.uuid4()
    service = MagicMock()
    service.ingest_resume_bytes = AsyncMock()
    outcome = await seed_one(
        sample=sample,
        content=b"%PDF-1.4 body",
        content_type="application/pdf",
        source="pdf",
        file_key="cvs/co/HR/1.pdf",
        resume_id=resume_id,
        service=service,
        skip_storage=True,
    )
    assert outcome.status == "seeded"
    assert outcome.resume_id == resume_id
    service.ingest_resume_bytes.assert_awaited_once()


def test_load_sample_content_prefers_pdf_then_text(tmp_path: Path) -> None:
    pdf = tmp_path / "1.pdf"
    pdf.write_bytes(b"%PDF-1.4 body")
    with_pdf = ResumeSample("1", "HR", "plain text", pdf)
    content, content_type, source = load_sample_content(with_pdf)
    assert source == "pdf"
    assert content_type == "application/pdf"
    assert content.startswith(b"%PDF-")

    text_only = ResumeSample("2", "HR", "  Resume string  ", None)
    content, content_type, source = load_sample_content(text_only)
    assert source == "text"
    assert content_type == "text/plain"
    assert content == b"Resume string"


def test_require_r2_without_credentials(monkeypatch: pytest.MonkeyPatch) -> None:
    from ai_engine.seeding.kaggle_seeder import _require_r2

    monkeypatch.setattr(
        kaggle_seeder,
        "r2_configured",
        lambda _settings=None: False,
    )
    with pytest.raises(SystemExit, match="R2 credentials"):
        _require_r2(skip_storage=False)
    _require_r2(skip_storage=True)


def test_upload_cv_to_r2(monkeypatch: pytest.MonkeyPatch) -> None:
    storage.reset_r2_client_cache()
    settings = Settings(
        storage_backend="r2",
        r2_access_key_id="access",
        r2_secret_access_key="secret",
        r2_account_id="abc123",
        r2_bucket_name="cvs",
    )
    monkeypatch.setattr(storage, "get_settings", lambda: settings)
    s3 = MagicMock()
    monkeypatch.setattr(storage.boto3, "client", lambda *_args, **_kwargs: s3)

    key = storage.upload_cv("/cvs/co/IT/1.pdf", b"%PDF-fake")
    assert key == "cvs/co/IT/1.pdf"
    s3.put_object.assert_called_once_with(
        Bucket="cvs",
        Key="cvs/co/IT/1.pdf",
        Body=b"%PDF-fake",
        ContentType="application/pdf",
    )


def test_upload_cv_to_supabase(monkeypatch: pytest.MonkeyPatch) -> None:
    settings = Settings(
        storage_backend="supabase",
        supabase_url="https://example.supabase.co",
        supabase_service_role_key="service",
    )
    monkeypatch.setattr(storage, "get_settings", lambda: settings)
    bucket = MagicMock()
    client = MagicMock()
    client.storage.from_.return_value = bucket
    monkeypatch.setattr(storage, "create_client", lambda *_args, **_kwargs: client)

    key = storage.upload_cv("cvs/co/IT/1.pdf", b"%PDF-fake", content_type="application/pdf")
    assert key == "cvs/co/IT/1.pdf"
    bucket.upload.assert_called_once_with(
        path="cvs/co/IT/1.pdf",
        file=b"%PDF-fake",
        file_options={"content-type": "application/pdf", "upsert": "true"},
    )
