import pymupdf as fitz
import pytest

from ai_engine.ingestion.parsers.docx_parser import parse_docx
from ai_engine.ingestion.parsers.pdf_parser import PdfParser
from ai_engine.ingestion.validation import InvalidCvError, detect_cv_format


def _minimal_pdf(text: str) -> bytes:
    doc = fitz.open()
    page = doc.new_page()
    page.insert_text((72, 72), text)
    content = doc.tobytes()
    doc.close()
    return content


def _minimal_docx() -> bytes:
    from io import BytesIO

    from docx import Document

    buffer = BytesIO()
    doc = Document()
    doc.add_paragraph("Experience")
    doc.add_paragraph("Built APIs with Python")
    doc.save(buffer)
    return buffer.getvalue()


@pytest.mark.asyncio
async def test_pdf_parser_native_text() -> None:
    body = "\n".join(
        [
            "Experience",
            "Software Engineer at ACME Corp",
            "Built APIs with Python and .NET for 5 years",
            "Education: BSc Computer Science",
        ]
    )
    content = _minimal_pdf(body)
    result = await PdfParser(llm=None).parse(content)
    assert "Software Engineer" in result.full_text
    assert result.parser_strategy in {"native_layout", "two_column_reordered"}


def test_docx_parser_extracts_paragraphs() -> None:
    result = parse_docx(_minimal_docx())
    assert "Built APIs" in result.full_text
    assert result.parser_strategy == "docx_native"


def test_detect_rejects_non_cv_bytes() -> None:
    with pytest.raises(InvalidCvError):
        detect_cv_format(b"not a cv", max_bytes=1024)
