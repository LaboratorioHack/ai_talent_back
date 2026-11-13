"""Re-read stored CVs whose structured profile is incomplete or from an older extractor.

Until extractor v1.2.0 the API stored the profile again after ingestion and kept only the summary
and the skills, so the generated CV lost its experience and education. This reads those files again
from storage and stores the full profile.

    python -m ai_engine.maintenance.reprocess_resumes --dry-run
    python -m ai_engine.maintenance.reprocess_resumes --limit 50

Each CV costs one extraction call and its embeddings, so it runs one by one and can be limited.
"""

from __future__ import annotations

import argparse
import asyncio
import uuid

from ai_engine.core.config import get_settings
from ai_engine.persistence.db import close_pool, connection
from ai_engine.services.ingest_service import IngestService

_OUTDATED_SQL = """
    select id
    from app.resumes
    where processing_status = 'PROCESSED'
      and (
        extractor_version is distinct from %(version)s
        or professional_data is null
        or jsonb_typeof(professional_data -> 'work_experience') is distinct from 'array'
      )
    order by created_at desc
    limit %(limit)s
"""

_ALL_SQL = """
    select id
    from app.resumes
    where processing_status = 'PROCESSED'
    order by created_at desc
    limit %(limit)s
"""


async def pending_resume_ids(*, limit: int, everything: bool) -> list[uuid.UUID]:
    params = {"version": get_settings().extractor_version, "limit": limit}
    async with connection() as conn:
        cursor = await conn.execute(_ALL_SQL if everything else _OUTDATED_SQL, params)
        return [row["id"] for row in await cursor.fetchall()]


async def reprocess(*, limit: int, everything: bool, dry_run: bool) -> tuple[int, int]:
    """Returns (reprocessed, failed)."""
    try:
        ids = await pending_resume_ids(limit=limit, everything=everything)
        print(f"{len(ids)} CV(s) to read again")
        if dry_run:
            return 0, 0

        service = IngestService()
        done = failed = 0
        for index, resume_id in enumerate(ids, start=1):
            try:
                result = await service.ingest_from_storage(resume_id)
            except Exception as exc:  # noqa: BLE001 - one unreadable file must not stop the rest
                failed += 1
                print(f"[{index}/{len(ids)}] {resume_id} FAILED: {type(exc).__name__}: {exc}")
                continue
            done += 1
            profile = result.professional_data
            print(
                f"[{index}/{len(ids)}] {resume_id} ok: "
                f"{len(profile.work_experience)} role(s), {len(profile.education)} degree(s)"
            )
        return done, failed
    finally:
        await close_pool()


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--limit", type=int, default=200, help="Most recent CVs to read (default 200)."
    )
    parser.add_argument(
        "--all", action="store_true", help="Every processed CV, not only incomplete ones."
    )
    parser.add_argument(
        "--dry-run", action="store_true", help="Only count the CVs that would be read."
    )
    args = parser.parse_args(argv)

    done, failed = asyncio.run(
        reprocess(limit=args.limit, everything=args.all, dry_run=args.dry_run)
    )
    if not args.dry_run:
        print(f"Done: {done} read again, {failed} failed")
    if failed:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
