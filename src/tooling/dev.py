"""Dev entrypoints: `uv run dev`, `uv run test-engine`, `uv run seed-resumes`."""

from __future__ import annotations

import os
import subprocess
import sys
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


def _load_env_file(path: Path) -> None:
    if not path.is_file():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        key = key.strip()
        if not key or key in os.environ:
            continue
        os.environ[key] = value.strip().strip('"').strip("'")


def seed_resumes() -> None:
    env_file = _ROOT / ".env.development"
    if not env_file.is_file():
        msg = f"Missing {env_file}. Copy from .env.example and fill in secrets."
        raise SystemExit(msg)
    _load_env_file(env_file)
    subprocess.run(
        ["uv", "run", "python", "-m", "ai_engine.seeding.kaggle_seeder", *sys.argv[1:]],
        cwd=_ROOT / "src" / "ai_engine",
        check=True,
    )


if __name__ == "__main__":
    main()
