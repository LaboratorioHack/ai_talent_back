"""Upload validation (OWASP A06 Insecure Design / A05 Injection).

Never trust the file extension or the client's Content-Type: check size and magic bytes.
DOCX is a ZIP container, so it is also checked for the Word main document part.
"""

import io
import zipfile
from enum import StrEnum


class CvFormat(StrEnum):
    PDF = "pdf"
    DOCX = "docx"


class InvalidCvError(ValueError):
    pass


_PDF_MAGIC = b"%PDF-"
_ZIP_MAGIC = b"PK\x03\x04"
_MAX_ZIP_ENTRIES = 2_000
_MAX_UNCOMPRESSED_BYTES = 100 * 1024 * 1024  # zip-bomb guard


def detect_cv_format(content: bytes, max_bytes: int) -> CvFormat:
    if not content:
        raise InvalidCvError("Empty file")
    if len(content) > max_bytes:
        raise InvalidCvError("File too large")

    if content.startswith(_PDF_MAGIC):
        return CvFormat.PDF

    if content.startswith(_ZIP_MAGIC):
        try:
            with zipfile.ZipFile(io.BytesIO(content)) as archive:
                entries = archive.infolist()
                if len(entries) > _MAX_ZIP_ENTRIES:
                    raise InvalidCvError("Too many entries in DOCX")
                if sum(entry.file_size for entry in entries) > _MAX_UNCOMPRESSED_BYTES:
                    raise InvalidCvError("DOCX expands too much")
                if "word/document.xml" in archive.namelist():
                    return CvFormat.DOCX
        except zipfile.BadZipFile as exc:
            raise InvalidCvError("Corrupted DOCX") from exc

    raise InvalidCvError("Unsupported format: only PDF and DOCX are accepted")
