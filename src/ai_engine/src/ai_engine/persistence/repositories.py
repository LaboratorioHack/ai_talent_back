"""Repositories mapped to supabase/migrations/20261111120000_core_data_model.sql."""

import uuid
from typing import Any

from psycopg.types.json import Jsonb

from ai_engine.persistence.db import connection


async def get_or_create_company(name: str, company_id: uuid.UUID | None = None) -> uuid.UUID:
    async with connection() as conn:
        if company_id is not None:
            row = await conn.execute("select id from app.companies where id = %s", (company_id,))
            existing = await row.fetchone()
            if existing:
                return existing["id"]
            await conn.execute(
                "insert into app.companies (id, name) values (%s, %s)",
                (company_id, name),
            )
            await conn.commit()
            return company_id
        row = await conn.execute(
            "select id from app.companies where name = %s order by created_at limit 1",
            (name,),
        )
        existing = await row.fetchone()
        if existing:
            return existing["id"]
        new_id = uuid.uuid4()
        await conn.execute(
            "insert into app.companies (id, name) values (%s, %s)",
            (new_id, name),
        )
        await conn.commit()
        return new_id


async def upsert_candidate(company_id: uuid.UUID, name: str, contact_email: str) -> uuid.UUID:
    async with connection() as conn:
        row = await conn.execute(
            """
            insert into app.candidates (company_id, name, contact_email)
            values (%s, %s, %s)
            on conflict (company_id, contact_email) do update
              set name = excluded.name
            returning id
            """,
            (company_id, name, contact_email),
        )
        result = await row.fetchone()
        await conn.commit()
        if result is None:
            msg = "upsert_candidate did not return an id"
            raise RuntimeError(msg)
        return result["id"]


async def get_resume_by_file_hash(
    company_id: uuid.UUID, file_hash: str
) -> dict[str, Any] | None:
    async with connection() as conn:
        row = await conn.execute(
            """
            select r.id, r.candidate_id, r.version, r.file_key, r.file_hash, r.processing_status
            from app.resumes r
            join app.candidates c on c.id = r.candidate_id
            where c.company_id = %s and r.file_hash = %s
            order by r.created_at desc
            limit 1
            """,
            (company_id, file_hash),
        )
        return await row.fetchone()


async def create_resume(
    candidate_id: uuid.UUID,
    file_key: str,
    file_hash: str,
    version: int = 1,
) -> uuid.UUID:
    async with connection() as conn:
        row = await conn.execute(
            """
            select coalesce(max(version), 0) as max_version
            from app.resumes
            where candidate_id = %s
            """,
            (candidate_id,),
        )
        existing = await row.fetchone()
        max_version = int(existing["max_version"]) if existing else 0
        next_version = max(version, max_version + 1)
        resume_id = uuid.uuid4()
        await conn.execute(
            """
            insert into app.resumes (
                id, candidate_id, version, file_key, file_hash, processing_status
            )
            values (%s, %s, %s, %s, %s, 'PENDING')
            """,
            (resume_id, candidate_id, next_version, file_key, file_hash),
        )
        await conn.commit()
        return resume_id


async def get_resume(resume_id: uuid.UUID) -> dict[str, Any] | None:
    async with connection() as conn:
        row = await conn.execute(
            """
            select id, candidate_id, version, file_key, file_hash, processing_status,
                   professional_data, extractor_version
            from app.resumes where id = %s
            """,
            (resume_id,),
        )
        return await row.fetchone()


async def update_resume_processing(
    resume_id: uuid.UUID,
    *,
    status: str,
    professional_data: dict[str, Any] | None = None,
    extractor_version: str | None = None,
    processing_error: str | None = None,
) -> None:
    async with connection() as conn:
        await conn.execute(
            """
            update app.resumes
            set processing_status = %s,
                professional_data = coalesce(%s, professional_data),
                extractor_version = coalesce(%s, extractor_version),
                processing_error = %s
            where id = %s
            """,
            (
                status,
                Jsonb(professional_data) if professional_data is not None else None,
                extractor_version,
                processing_error,
                resume_id,
            ),
        )
        await conn.commit()


async def delete_resume_fragments(resume_id: uuid.UUID) -> None:
    async with connection() as conn:
        await conn.execute("delete from app.resume_fragments where resume_id = %s", (resume_id,))
        await conn.commit()


def _fragment_row(resume_id: uuid.UUID, frag: dict[str, Any]) -> tuple[Any, ...]:
    frag_id = frag.get("id") or uuid.uuid4()
    return (
        frag_id,
        resume_id,
        frag["position"],
        frag.get("section"),
        Jsonb(frag.get("source_location") or {}),
        frag["anonymized_content"],
        frag.get("embedding"),
        frag.get("embedding_model"),
    )


_INSERT_FRAGMENT_SQL = """
    insert into app.resume_fragments (
        id, resume_id, position, section, source_location,
        anonymized_content, embedding, embedding_model
    ) values (%s, %s, %s, %s, %s, %s, %s, %s)
"""


async def insert_resume_fragments(
    resume_id: uuid.UUID,
    fragments: list[dict[str, Any]],
) -> list[uuid.UUID]:
    rows = [_fragment_row(resume_id, frag) for frag in fragments]
    ids = [row[0] for row in rows]
    if not rows:
        return ids
    async with connection() as conn:
        async with conn.cursor() as cur:
            await cur.executemany(_INSERT_FRAGMENT_SQL, rows)
        await conn.commit()
    return ids


async def persist_ingested_resume(
    resume_id: uuid.UUID,
    *,
    fragments: list[dict[str, Any]],
    professional_data: dict[str, Any],
    extractor_version: str,
    parser_strategy: str,
) -> list[uuid.UUID]:
    rows = [_fragment_row(resume_id, frag) for frag in fragments]
    ids = [row[0] for row in rows]
    async with connection() as conn:
        await conn.execute("delete from app.resume_fragments where resume_id = %s", (resume_id,))
        if rows:
            async with conn.cursor() as cur:
                await cur.executemany(_INSERT_FRAGMENT_SQL, rows)
        await conn.execute(
            """
            update app.resumes
            set processing_status = %s,
                professional_data = coalesce(%s, professional_data),
                extractor_version = coalesce(%s, extractor_version),
                processing_error = %s
            where id = %s
            """,
            (
                "PROCESSED",
                Jsonb(professional_data),
                extractor_version,
                None,
                resume_id,
            ),
        )
        await conn.execute(
            """
            insert into app.audit_log (actor_type, action, entity, entity_id, details)
            values ('engine', %s, %s, %s, %s)
            """,
            (
                "resume.processed",
                "resume",
                str(resume_id),
                Jsonb({"chunks": len(fragments), "parser_strategy": parser_strategy}),
            ),
        )
        await conn.commit()
    return ids


async def bulk_prepare_seed_rows(
    company_id: uuid.UUID,
    rows: list[dict[str, Any]],
    *,
    force: bool = False,
) -> list[dict[str, Any]]:
    """Upsert candidates + resumes in one transaction.

    Each input row needs ``name``, ``contact_email``, ``file_key``, ``file_hash``.
    Returns the same rows with ``candidate_id``, ``resume_id``, and ``skipped``.
    """
    if not rows:
        return []
    hashes = [str(row["file_hash"]) for row in rows]
    emails = [str(row["contact_email"]) for row in rows]
    names = [str(row["name"]) for row in rows]
    async with connection() as conn:
        existing_cur = await conn.execute(
            """
            select r.id, r.candidate_id, r.file_hash, r.processing_status
            from app.resumes r
            join app.candidates c on c.id = r.candidate_id
            where c.company_id = %s and r.file_hash = any(%s)
            """,
            (company_id, hashes),
        )
        existing_by_hash = {row["file_hash"]: row for row in await existing_cur.fetchall()}

        upsert_cur = await conn.execute(
            """
            insert into app.candidates (company_id, name, contact_email)
            select %s, x.name, x.email
            from unnest(%s::text[], %s::text[]) as x(name, email)
            on conflict (company_id, contact_email) do update
              set name = excluded.name
            returning id, contact_email
            """,
            (company_id, names, emails),
        )
        candidate_by_email = {
            row["contact_email"]: row["id"] for row in await upsert_cur.fetchall()
        }
        candidate_ids = list(candidate_by_email.values())
        version_cur = await conn.execute(
            """
            select candidate_id, coalesce(max(version), 0) as max_version
            from app.resumes
            where candidate_id = any(%s)
            group by candidate_id
            """,
            (candidate_ids,),
        )
        next_version: dict[uuid.UUID, int] = {
            row["candidate_id"]: int(row["max_version"]) + 1
            for row in await version_cur.fetchall()
        }

        prepared: list[dict[str, Any]] = []
        new_resumes: list[tuple[Any, ...]] = []
        for row in rows:
            file_hash = str(row["file_hash"])
            email = str(row["contact_email"])
            candidate_id = candidate_by_email[email]
            existing = existing_by_hash.get(file_hash)
            skipped = bool(
                existing
                and existing.get("processing_status") == "PROCESSED"
                and not force
            )
            if existing:
                resume_id = existing["id"]
            else:
                resume_id = uuid.uuid4()
                version = next_version.get(candidate_id, 1)
                next_version[candidate_id] = version + 1
                new_resumes.append(
                    (resume_id, candidate_id, version, row["file_key"], file_hash)
                )
            prepared.append(
                {
                    **row,
                    "candidate_id": candidate_id,
                    "resume_id": resume_id,
                    "skipped": skipped,
                }
            )
        if new_resumes:
            async with conn.cursor() as cur:
                await cur.executemany(
                    """
                    insert into app.resumes (
                        id, candidate_id, version, file_key, file_hash, processing_status
                    )
                    values (%s, %s, %s, %s, %s, 'PENDING')
                    """,
                    new_resumes,
                )
        await conn.commit()
    return prepared


async def get_role_profile(role_profile_id: uuid.UUID) -> dict[str, Any] | None:
    async with connection() as conn:
        row = await conn.execute(
            """
            select id, job_opening_id, version, description, evaluation_policy
            from app.role_profiles where id = %s
            """,
            (role_profile_id,),
        )
        return await row.fetchone()


async def list_requirements(role_profile_id: uuid.UUID) -> list[dict[str, Any]]:
    async with connection() as conn:
        cur = await conn.execute(
            """
            select id, type, category, description, condition, weight, embedding, embedding_model
            from app.requirements where role_profile_id = %s
            order by type, weight desc
            """,
            (role_profile_id,),
        )
        return list(await cur.fetchall())


async def get_application(application_id: uuid.UUID) -> dict[str, Any] | None:
    async with connection() as conn:
        row = await conn.execute(
            "select id, job_opening_id, candidate_id from app.applications where id = %s",
            (application_id,),
        )
        return await row.fetchone()


async def get_or_create_application(
    job_opening_id: uuid.UUID,
    candidate_id: uuid.UUID,
    source: str = "COMPANY_UPLOAD",
) -> uuid.UUID:
    async with connection() as conn:
        row = await conn.execute(
            """
            select id from app.applications
            where job_opening_id = %s and candidate_id = %s
            """,
            (job_opening_id, candidate_id),
        )
        existing = await row.fetchone()
        if existing:
            return existing["id"]
        app_id = uuid.uuid4()
        await conn.execute(
            """
            insert into app.applications (id, job_opening_id, candidate_id, source)
            values (%s, %s, %s, %s)
            """,
            (app_id, job_opening_id, candidate_id, source),
        )
        await conn.commit()
        return app_id


async def list_resume_fragments(resume_id: uuid.UUID) -> list[dict[str, Any]]:
    async with connection() as conn:
        cur = await conn.execute(
            """
            select id, resume_id, position, section, source_location,
                   anonymized_content, embedding, embedding_model
            from app.resume_fragments
            where resume_id = %s
            order by position
            """,
            (resume_id,),
        )
        return list(await cur.fetchall())


async def search_fragments_by_embedding(
    resume_id: uuid.UUID,
    query_embedding: list[float],
    *,
    limit: int = 8,
) -> list[dict[str, Any]]:
    async with connection() as conn:
        cur = await conn.execute(
            """
            select id, section, source_location, anonymized_content,
                   embedding_model,
                   1 - (embedding <=> %s::extensions.vector) as similarity
            from app.resume_fragments
            where resume_id = %s and embedding is not null
            order by embedding <=> %s::extensions.vector
            limit %s
            """,
            (query_embedding, resume_id, query_embedding, limit),
        )
        return list(await cur.fetchall())


async def get_latest_evaluation(
    role_profile_id: uuid.UUID, resume_id: uuid.UUID
) -> dict[str, Any] | None:
    async with connection() as conn:
        row = await conn.execute(
            """
            select id, application_id, role_profile_id, resume_id, processing_status,
                   total_score, fulfillment, disqualified, confidence,
                   strengths, gaps, recommend_interview, ai_model, prompt_version, created_at
            from app.evaluations
            where role_profile_id = %s and resume_id = %s and processing_status = 'COMPLETED'
            order by created_at desc
            limit 1
            """,
            (role_profile_id, resume_id),
        )
        return await row.fetchone()


async def list_requirement_results(evaluation_id: uuid.UUID) -> list[dict[str, Any]]:
    async with connection() as conn:
        cur = await conn.execute(
            """
            select rr.id, rr.evaluation_id, rr.requirement_id, rr.result, rr.score,
                   rr.confidence, rr.justification,
                   req.type, req.category, req.description, req.condition, req.weight
            from app.requirement_results rr
            join app.requirements req on req.id = rr.requirement_id
            where rr.evaluation_id = %s
            order by req.type, req.weight desc
            """,
            (evaluation_id,),
        )
        return list(await cur.fetchall())


async def insert_evaluation(
    *,
    application_id: uuid.UUID,
    role_profile_id: uuid.UUID,
    resume_id: uuid.UUID,
    total_score: float,
    fulfillment: str,
    disqualified: bool,
    confidence: float,
    strengths: list[Any],
    gaps: list[Any],
    recommend_interview: bool,
    ai_model: str,
    prompt_version: str,
) -> uuid.UUID:
    eval_id = uuid.uuid4()
    async with connection() as conn:
        await conn.execute(
            """
            insert into app.evaluations (
                id, application_id, role_profile_id, resume_id, processing_status,
                total_score, fulfillment, disqualified, confidence,
                strengths, gaps, recommend_interview, ai_model, prompt_version
            ) values (
                %s, %s, %s, %s, 'COMPLETED',
                %s, %s, %s, %s, %s, %s, %s, %s, %s
            )
            """,
            (
                eval_id,
                application_id,
                role_profile_id,
                resume_id,
                total_score,
                fulfillment,
                disqualified,
                confidence,
                Jsonb(strengths),
                Jsonb(gaps),
                recommend_interview,
                ai_model,
                prompt_version,
            ),
        )
        await conn.commit()
    return eval_id


async def insert_requirement_result(
    *,
    evaluation_id: uuid.UUID,
    requirement_id: uuid.UUID,
    result: str,
    score: float,
    confidence: float,
    justification: str,
) -> uuid.UUID:
    rr_id = uuid.uuid4()
    async with connection() as conn:
        await conn.execute(
            """
            insert into app.requirement_results (
                id, evaluation_id, requirement_id, result, score, confidence, justification
            ) values (%s, %s, %s, %s, %s, %s, %s)
            """,
            (rr_id, evaluation_id, requirement_id, result, score, confidence, justification),
        )
        await conn.commit()
    return rr_id


async def insert_evidence(
    *,
    requirement_result_id: uuid.UUID,
    resume_fragment_id: uuid.UUID,
    citation: str,
) -> uuid.UUID:
    ev_id = uuid.uuid4()
    async with connection() as conn:
        await conn.execute(
            """
            insert into app.evidence (id, requirement_result_id, resume_fragment_id, citation)
            values (%s, %s, %s, %s)
            """,
            (ev_id, requirement_result_id, resume_fragment_id, citation),
        )
        await conn.commit()
    return ev_id


async def append_audit_log(
    *,
    action: str,
    entity: str,
    entity_id: str,
    details: dict[str, Any] | None = None,
) -> None:
    async with connection() as conn:
        await conn.execute(
            """
            insert into app.audit_log (actor_type, action, entity, entity_id, details)
            values ('engine', %s, %s, %s, %s)
            """,
            (action, entity, entity_id, Jsonb(details or {})),
        )
        await conn.commit()
