"""Dev entrypoints: `uv run dev`, `uv run test-engine` (from repo root)."""

from __future__ import annotations

import subprocess
from pathlib import Path

_ROOT = Path(__file__).resolve().parents[2]


def _run(script: str) -> None:
    path = _ROOT / "scripts" / script
    if not path.is_file():
        msg = f"Missing script: {path}"
        raise SystemExit(msg)
    subprocess.run(["bash", str(path)], cwd=_ROOT, check=True)


def main() -> None:
    _run("dev-stack.sh")


def test_engine() -> None:
    subprocess.run(
        ["uv", "run", "pytest", "-q"],
        cwd=_ROOT / "src" / "ai_engine",
        check=True,
    )


if __name__ == "__main__":
    main()
