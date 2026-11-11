import io
import zipfile

import pytest
from fastapi import Depends, FastAPI
from fastapi.testclient import TestClient

from ai_engine.core.config import Settings, get_settings
from ai_engine.core.security import require_internal_api_key
from ai_engine.ingestion.validation import CvFormat, InvalidCvError, detect_cv_format

MAX = 1024 * 1024


def _docx_bytes() -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr("word/document.xml", "<w:document/>")
    return buffer.getvalue()


def test_detects_pdf() -> None:
    assert detect_cv_format(b"%PDF-1.7 ...", MAX) is CvFormat.PDF


def test_detects_docx() -> None:
    assert detect_cv_format(_docx_bytes(), MAX) is CvFormat.DOCX


@pytest.mark.parametrize(
    "content",
    [b"", b"MZ\x90\x00 fake exe renamed to .pdf", b"<script>alert(1)</script>"],
)
def test_rejects_invalid_files(content: bytes) -> None:
    with pytest.raises(InvalidCvError):
        detect_cv_format(content, MAX)


def test_rejects_too_large() -> None:
    with pytest.raises(InvalidCvError):
        detect_cv_format(b"%PDF-" + b"0" * 20, max_bytes=10)


def _protected_client() -> TestClient:
    app = FastAPI()

    @app.get("/secret", dependencies=[Depends(require_internal_api_key)])
    async def secret() -> dict[str, str]:
        return {"ok": "yes"}

    app.dependency_overrides[get_settings] = lambda: Settings(engine_api_key="s3cret")
    return TestClient(app)


def test_internal_api_key_required() -> None:
    client = _protected_client()
    assert client.get("/secret").status_code == 401
    assert client.get("/secret", headers={"X-Internal-Api-Key": "wrong"}).status_code == 401
    assert client.get("/secret", headers={"X-Internal-Api-Key": "s3cret"}).status_code == 200
