"""PDF parsing: native text, two-column reorder, vision OCR hooks."""

from __future__ import annotations

import re
from dataclasses import dataclass, field

import pymupdf as fitz

from ai_engine.core.llm import LlmClient


@dataclass
class ParsedPage:
    page_number: int
    text: str
    used_vision: bool = False
    ocr_confidence: float | None = None


@dataclass
class PdfParseResult:
    pages: list[ParsedPage] = field(default_factory=list)
    full_text: str = ""
    parser_strategy: str = "native_layout"
    mean_ocr_confidence: float | None = None


class PdfParser:
    def __init__(self, llm: LlmClient | None = None) -> None:
        self._llm = llm

    async def parse(self, content: bytes) -> PdfParseResult:
        doc = fitz.open(stream=content, filetype="pdf")
        pages: list[ParsedPage] = []
        strategies: set[str] = set()
        confidences: list[float] = []

        try:
            for index in range(doc.page_count):
                page = doc[index]
                page_num = index + 1
                if self._needs_vision(page):
                    text, conf = await self._vision_page(page, page_num)
                    pages.append(
                        ParsedPage(
                            page_number=page_num, text=text, used_vision=True, ocr_confidence=conf
                        )
                    )
                    strategies.add("vision_ocr")
                    confidences.append(conf)
                else:
                    text, strategy = self._extract_page_text(page)
                    pages.append(ParsedPage(page_number=page_num, text=text))
                    strategies.add(strategy)
        finally:
            doc.close()

        full_text = "\n\n".join(p.text for p in pages if p.text.strip())
        parser_strategy = _combine_strategy(strategies)
        mean_conf = sum(confidences) / len(confidences) if confidences else None
        return PdfParseResult(
            pages=pages,
            full_text=full_text,
            parser_strategy=parser_strategy,
            mean_ocr_confidence=mean_conf,
        )

    def _needs_vision(self, page: fitz.Page) -> bool:
        native = page.get_text().strip()
        if len(native) < 50:
            return True
        if native.count("\ufffd") / max(len(native), 1) > 0.05:
            return True
        blocks = page.get_text("dict").get("blocks", [])
        image_area = 0.0
        total_area = page.rect.width * page.rect.height or 1.0
        for block in blocks:
            if block.get("type") == 1:
                x0, y0, x1, y1 = block["bbox"]
                image_area += max(0.0, (x1 - x0) * (y1 - y0))
        return image_area / total_area > 0.6

    def _extract_page_text(self, page: fitz.Page) -> tuple[str, str]:
        blocks = page.get_text("blocks")
        if _is_two_column(blocks, page.rect.width):
            text = _read_two_columns(blocks, page.rect.width)
            return text, "two_column_reordered"
        return page.get_text(), "native_layout"

    async def _vision_page(self, page: fitz.Page, page_number: int) -> tuple[str, float]:
        if self._llm is None:
            return page.get_text(), 0.3
        pix = page.get_pixmap(dpi=300)
        png = pix.tobytes("png")
        return await self._llm.transcribe_page_image(png, page_number=page_number)


def _is_two_column(blocks: list, page_width: float) -> bool:
    mids: list[float] = []
    for block in blocks:
        if len(block) < 5 or not str(block[4]).strip():
            continue
        x0, _y0, x1, _y1, _text = block[:5]
        mids.append((x0 + x1) / 2)
    if len(mids) < 6:
        return False
    left = sum(1 for m in mids if m < page_width * 0.45)
    right = sum(1 for m in mids if m > page_width * 0.55)
    return left >= 3 and right >= 3


def _read_two_columns(blocks: list, page_width: float) -> str:
    left: list[tuple[float, float, str]] = []
    right: list[tuple[float, float, str]] = []
    split = page_width / 2
    for block in blocks:
        if len(block) < 5:
            continue
        x0, y0, x1, _y1, text = block[:5]
        snippet = re.sub(r"\s+", " ", str(text)).strip()
        if not snippet:
            continue
        mid = (x0 + x1) / 2
        target = right if mid >= split else left
        target.append((y0, x0, snippet))

    def _join(col: list[tuple[float, float, str]]) -> str:
        col.sort(key=lambda t: (t[0], t[1]))
        return "\n".join(t[2] for t in col)

    return f"{_join(left)}\n\n{_join(right)}"


def _combine_strategy(strategies: set[str]) -> str:
    if "vision_ocr" in strategies and "two_column_reordered" in strategies:
        return "vision_ocr+two_column"
    if "vision_ocr" in strategies:
        return "vision_ocr"
    if "two_column_reordered" in strategies:
        return "two_column_reordered"
    return "native_layout"
