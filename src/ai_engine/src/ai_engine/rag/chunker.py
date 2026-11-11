"""Semantic-ish chunking with page/offset traceability."""

from __future__ import annotations

import re
from dataclasses import dataclass

_SECTION_HINTS = {
    "experience": re.compile(r"(experiencia|experience|employment|trabajo)", re.I),
    "education": re.compile(r"(educaci[oó]n|education|formaci[oó]n)", re.I),
    "skills": re.compile(r"(habilidades|skills|competencias|technologies)", re.I),
    "projects": re.compile(r"(proyectos|projects)", re.I),
}


@dataclass
class TextChunk:
    position: int
    section: str | None
    content: str
    source_location: dict


def chunk_cv_text(
    text: str,
    *,
    pages: list[tuple[int, str]] | None = None,
    max_chars: int = 900,
) -> list[TextChunk]:
    if pages:
        return _chunk_by_pages(pages, max_chars=max_chars)
    return _chunk_flat(text, max_chars=max_chars)


def _chunk_by_pages(pages: list[tuple[int, str]], *, max_chars: int) -> list[TextChunk]:
    chunks: list[TextChunk] = []
    position = 0
    offset = 0
    for page_number, page_text in pages:
        section = _detect_section(page_text)
        for piece in _split_paragraphs(page_text, max_chars):
            chunks.append(
                TextChunk(
                    position=position,
                    section=section,
                    content=piece,
                    source_location={
                        "page_number": page_number,
                        "char_start": offset,
                        "char_end": offset + len(piece),
                    },
                )
            )
            offset += len(piece) + 1
            position += 1
    return chunks


def _chunk_flat(text: str, *, max_chars: int) -> list[TextChunk]:
    chunks: list[TextChunk] = []
    offset = 0
    for position, piece in enumerate(_split_paragraphs(text, max_chars)):
        chunks.append(
            TextChunk(
                position=position,
                section=_detect_section(piece),
                content=piece,
                source_location={"char_start": offset, "char_end": offset + len(piece)},
            )
        )
        offset += len(piece) + 1
    return chunks


def _split_paragraphs(text: str, max_chars: int) -> list[str]:
    paragraphs = [p.strip() for p in re.split(r"\n\s*\n", text) if p.strip()]
    if not paragraphs:
        return [text.strip()] if text.strip() else []
    merged: list[str] = []
    buf = ""
    for para in paragraphs:
        if len(para) > max_chars:
            if buf:
                merged.append(buf)
                buf = ""
            merged.extend(_hard_split(para, max_chars))
            continue
        candidate = f"{buf}\n\n{para}".strip() if buf else para
        if len(candidate) <= max_chars:
            buf = candidate
        else:
            merged.append(buf)
            buf = para
    if buf:
        merged.append(buf)
    return merged


def _hard_split(text: str, max_chars: int) -> list[str]:
    return [text[i : i + max_chars] for i in range(0, len(text), max_chars)]


def _detect_section(text: str) -> str | None:
    for name, pattern in _SECTION_HINTS.items():
        if pattern.search(text[:200]):
            return name
    return None
