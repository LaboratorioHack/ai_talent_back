"""Executive summary refinement for recruiters."""

import json

from ai_engine.core.llm import LlmClient

_SYSTEM = """You refine evaluation explanations for hiring managers.
Keep strengths and gaps concise, cite evidence, and explain confidence in plain language."""


class ExplainerAgent:
    def __init__(self, llm: LlmClient | None = None) -> None:
        self._llm = llm or LlmClient()

    async def refine_summary(self, evaluation_payload: dict) -> str:
        raw = await self._llm.complete_text(
            _SYSTEM,
            json.dumps(evaluation_payload, default=str)[:50_000],
            max_tokens=1024,
        )
        return raw.strip()
