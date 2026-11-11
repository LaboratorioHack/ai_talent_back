-- =====================================================================================
-- Security baseline (OWASP Top 10:2025)
--  A01 Broken Access Control  -> private `app` schema, deny-by-default grants, RLS on
--  A02 Security Misconfig     -> nothing auto-exposed through the Data API
--  A09 Logging & Alerting     -> append-only audit log (evaluations must be auditable)
-- =====================================================================================

create extension if not exists vector with schema extensions;

-- ---------------------------------------------------------------------------
-- Private schema: NOT listed in [api].schemas, so PostgREST never exposes it.
-- ---------------------------------------------------------------------------
create schema if not exists app;

revoke all on schema app from public, anon, authenticated;
revoke create on schema public from public;

-- ---------------------------------------------------------------------------
-- Least-privilege group roles (NOLOGIN). Create a LOGIN user per service that
-- inherits from one of these, outside of migrations (passwords never in git):
--   create role ai_talent_api_user login password '...' in role app_api;
--   create role ai_talent_engine_user login password '...' in role app_engine;
-- ---------------------------------------------------------------------------
do $$
begin
  if not exists (select 1 from pg_roles where rolname = 'app_api') then
    create role app_api nologin;
  end if;
  if not exists (select 1 from pg_roles where rolname = 'app_engine') then
    create role app_engine nologin;
  end if;
end
$$;

grant usage on schema app to app_api, app_engine;
grant usage on schema extensions to app_api, app_engine;

-- Future tables created by `postgres` in `app`: CRUD for both services, nothing for anon/authenticated.
alter default privileges for role postgres in schema app
  revoke all on tables from public, anon, authenticated;
alter default privileges for role postgres in schema app
  grant select, insert, update, delete on tables to app_api, app_engine;
alter default privileges for role postgres in schema app
  grant usage, select on sequences to app_api, app_engine;
alter default privileges for role postgres in schema app
  revoke execute on functions from public;

-- ---------------------------------------------------------------------------
-- Audit log: append-only (no UPDATE/DELETE, even for the services).
-- Never store raw PII here; reference entities by id.
-- ---------------------------------------------------------------------------
create table app.audit_log (
  id          bigint generated always as identity primary key,
  occurred_at timestamptz not null default now(),
  actor_id    uuid,                -- auth.users.id of the recruiter, null for system jobs
  actor_type  text not null check (actor_type in ('user', 'api', 'engine', 'agent')),
  action      text not null,       -- e.g. 'evaluation.created', 'ranking.viewed', 'cv.downloaded'
  entity      text not null,
  entity_id   text,
  details     jsonb not null default '{}'::jsonb
);

create index audit_log_entity_idx on app.audit_log (entity, entity_id);
create index audit_log_occurred_at_idx on app.audit_log (occurred_at desc);

-- Not FORCEd: the owner (postgres, also used by the services until the least-privilege users
-- exist) must be able to insert. Append-only is guaranteed by the trigger below anyway.
alter table app.audit_log enable row level security;

revoke all on app.audit_log from app_api, app_engine;
grant select, insert on app.audit_log to app_api, app_engine;
grant usage on sequence app.audit_log_id_seq to app_api, app_engine;

create policy audit_log_insert on app.audit_log
  for insert to app_api, app_engine with check (true);
create policy audit_log_select on app.audit_log
  for select to app_api using (true);

create function app.prevent_audit_log_mutation()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  raise exception 'app.audit_log is append-only';
end;
$$;

create trigger audit_log_append_only
  before update or delete or truncate on app.audit_log
  for each statement execute function app.prevent_audit_log_mutation();
