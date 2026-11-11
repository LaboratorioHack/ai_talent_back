"""Technology equivalence and transferability hints for retrieval expansion."""

from __future__ import annotations

_EQUIVALENCE_GROUPS: list[set[str]] = [
    {"postgresql", "postgres", "rds", "aurora", "cloud sql"},
    {"aws", "amazon web services"},
    {"azure", "microsoft azure"},
    {"gcp", "google cloud"},
    {"ecs", "fargate", "kubernetes", "k8s", "aks", "eks", "container apps"},
    {"java", "kotlin", "spring", "spring boot"},
    {"csharp", "c#", ".net", "dotnet", "asp.net"},
    {"react", "next.js", "nextjs"},
    {"typescript", "javascript", "node", "nodejs"},
]

_TRANSFERABILITY: dict[tuple[str, str], float] = {
    ("aws", "azure"): 0.35,
    ("aws", "gcp"): 0.4,
    ("azure", "gcp"): 0.4,
    ("react", "react native"): 0.25,
    ("java", "csharp"): 0.75,
    ("postgresql", "mysql"): 0.65,
}


def expand_query_terms(query: str) -> str:
    tokens = _normalize_tokens(query)
    extra: set[str] = set(tokens)
    for token in list(tokens):
        for group in _EQUIVALENCE_GROUPS:
            if token in group:
                extra.update(group)
    if not extra:
        return query
    return f"{query}\nRelated terms: {', '.join(sorted(extra))}"


def transferability(skill_a: str, skill_b: str) -> float:
    a, b = skill_a.lower().strip(), skill_b.lower().strip()
    if a == b:
        return 1.0
    for group in _EQUIVALENCE_GROUPS:
        if a in group and b in group:
            return 0.9
    return _TRANSFERABILITY.get((a, b), _TRANSFERABILITY.get((b, a), 0.0))


def _normalize_tokens(text: str) -> set[str]:
    return {
        t.strip().lower() for t in text.replace("/", " ").replace(",", " ").split() if len(t) > 2
    }
