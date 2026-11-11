"""DOCX text extraction."""

from dataclasses import dataclass
from io import BytesIO

from docx import Document


@dataclass
class DocxParseResult:
    full_text: str
    parser_strategy: str = "docx_native"


def parse_docx(content: bytes) -> DocxParseResult:
    doc = Document(BytesIO(content))
    paragraphs = [p.text.strip() for p in doc.paragraphs if p.text.strip()]
    return DocxParseResult(full_text="\n".join(paragraphs))
