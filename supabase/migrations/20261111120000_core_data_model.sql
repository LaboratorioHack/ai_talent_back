-- =====================================================================================
-- Core data model - AI Talent Scout
-- Based on the team's proposal, adapted to the project's architecture:
--   * Private `app` schema (never exposed through the Data API)           -> OWASP A01
--   * Users/passwords live in Supabase Auth (auth.users), not here         -> OWASP A07
--   * Candidate PII isolated in app.candidates; everything the LLM or the
--     embeddings see is anonymized                                         -> IA responsable
--   * Evaluations are an audit trail: deleting a profile/requirement/resume
--     that was used in an evaluation is blocked (NO ACTION)                -> auditable
--   * Erasing a candidate (data-subject request) cascades through all their
--     data, evaluations included                                           -> Habeas Data
--   * Embeddings: Voyage AI (1024 dims), not OpenAI (1536)
-- Requires 20261111000000_security_baseline.sql (schema app, roles, vector extension).
-- =====================================================================================

-- -------------------------------------------------------------------------------------
-- Helpers
-- -------------------------------------------------------------------------------------
create function app.set_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

-- -------------------------------------------------------------------------------------
-- Companies & recruiters
-- -------------------------------------------------------------------------------------
create table app.companies (
  id         uuid primary key default gen_random_uuid(),
  name       text not null,
  created_at timestamptz not null default now()
);

-- A recruiter is a Supabase Auth user that belongs to one company.
create table app.interviewers (
  user_id    uuid primary key references auth.users (id) on delete cascade,
  company_id uuid not null references app.companies (id),
  name       text not null,
  role       text not null check (role in ('ADMIN', 'RECRUITER')),
  active     boolean not null default true,
  created_at timestamptz not null default now()
);

-- Candidate PII lives ONLY here. Never send these columns to the LLM or embed them.
-- Scoped per company so one company can never see another company's candidates.
create table app.candidates (
  id            uuid primary key default gen_random_uuid(),
  company_id    uuid not null references app.companies (id),
  user_id       uuid unique references auth.users (id) on delete set null, -- only if the candidate logs in (AI interview)
  name          text not null,
  contact_email text not null,
  created_at    timestamptz not null default now(),
  unique (company_id, contact_email)
);

-- -------------------------------------------------------------------------------------
-- Job openings, versioned role profiles & requirements
-- -------------------------------------------------------------------------------------
create table app.job_openings (
  id         uuid primary key default gen_random_uuid(),
  company_id uuid not null references app.companies (id),
  creator_id uuid references app.interviewers (user_id) on delete set null,
  title      text not null,
  status     text not null default 'DRAFT' check (status in ('DRAFT', 'OPEN', 'CLOSED')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

-- Profiles are immutable once used: a change creates a new version.
create table app.role_profiles (
  id                uuid primary key default gen_random_uuid(),
  job_opening_id    uuid not null references app.job_openings (id) on delete cascade,
  version           int not null default 1 check (version > 0),
  description       text,
  evaluation_policy jsonb not null default '{}'::jsonb,
  created_at        timestamptz not null default now(),
  unique (job_opening_id, version)
);

create table app.requirements (
  id              uuid primary key default gen_random_uuid(),
  role_profile_id uuid not null references app.role_profiles (id) on delete cascade,
  type            text not null check (type in ('MANDATORY', 'DESIRABLE', 'DISQUALIFYING')),
  category        text,           -- e.g. EXPERIENCE, EDUCATION, CERTIFICATION, LANGUAGE, SKILL
  description     text not null,
  condition       text,           -- e.g. ">= 3 years", "AWS or GCP (Azure does not count)"
  weight          numeric not null default 1 check (weight >= 0),
  embedding       extensions.vector(1024),
  embedding_model text,
  check ((embedding is null) = (embedding_model is null))
);

-- -------------------------------------------------------------------------------------
-- Resumes & RAG fragments (anonymized)
-- -------------------------------------------------------------------------------------
create table app.resumes (
  id                uuid primary key default gen_random_uuid(),
  candidate_id      uuid not null references app.candidates (id) on delete cascade,
  version           int not null default 1 check (version > 0),
  file_key          text not null,  -- path inside the private Storage bucket `cvs`
  file_hash         text,           -- sha256, detects duplicate uploads
  processing_status text not null default 'PENDING'
                    check (processing_status in ('PENDING', 'PROCESSING', 'PROCESSED', 'FAILED')),
  processing_error  text,
  professional_data jsonb,          -- ANONYMIZED structured extraction (experience, education, ...)
  extractor_version text,
  created_at        timestamptz not null default now(),
  unique (candidate_id, version)
);

create table app.resume_fragments (
  id                 uuid primary key default gen_random_uuid(),
  resume_id          uuid not null references app.resumes (id) on delete cascade,
  position           int not null check (position >= 0),
  section            text,
  source_location    jsonb,         -- page / paragraph, to cite evidence
  anonymized_content text not null,
  embedding          extensions.vector(1024),
  embedding_model    text,
  unique (resume_id, position),
  check ((embedding is null) = (embedding_model is null))
);

-- -------------------------------------------------------------------------------------
-- Applications & evaluations (audit trail)
-- -------------------------------------------------------------------------------------
create table app.applications (
  id               uuid primary key default gen_random_uuid(),
  job_opening_id   uuid not null references app.job_openings (id),
  candidate_id     uuid not null references app.candidates (id) on delete cascade,
  source           text not null check (source in ('CANDIDATE', 'COMPANY_UPLOAD')),
  selection_status text not null default 'APPLIED'
                   check (selection_status in ('APPLIED', 'SHORTLISTED', 'INTERVIEW', 'REJECTED', 'HIRED', 'WITHDRAWN')),
  created_at       timestamptz not null default now(),
  updated_at       timestamptz not null default now(),
  unique (job_opening_id, candidate_id)
);

-- Snapshot of WHICH profile version and WHICH resume version were evaluated.
create table app.evaluations (
  id                  uuid primary key default gen_random_uuid(),
  application_id      uuid not null references app.applications (id) on delete cascade,
  role_profile_id     uuid not null references app.role_profiles (id),
  -- DEFERRABLE: checked at commit, so erasing a candidate (which cascades both to resumes and,
  -- via applications, to evaluations) succeeds; deleting an evaluated resume alone still fails.
  resume_id           uuid not null references app.resumes (id) deferrable initially deferred,
  processing_status   text not null default 'PENDING'
                      check (processing_status in ('PENDING', 'PROCESSING', 'COMPLETED', 'FAILED')),
  total_score         numeric check (total_score between 0 and 100),
  fulfillment         text check (fulfillment in ('MEETS', 'PARTIALLY_MEETS', 'DOES_NOT_MEET')),
  disqualified        boolean not null default false,
  confidence          numeric check (confidence between 0 and 1),
  strengths           jsonb,
  gaps                jsonb,
  recommend_interview boolean,
  ai_model            text,
  prompt_version      text,
  created_at          timestamptz not null default now()
);

create table app.requirement_results (
  id             uuid primary key default gen_random_uuid(),
  evaluation_id  uuid not null references app.evaluations (id) on delete cascade,
  requirement_id uuid not null references app.requirements (id),
  result         text not null check (result in ('MEETS', 'PARTIALLY_MEETS', 'DOES_NOT_MEET', 'NO_EVIDENCE')),
  score          numeric check (score between 0 and 100),
  confidence     numeric check (confidence between 0 and 1),
  justification  text,
  unique (evaluation_id, requirement_id)
);

create table app.evidence (
  id                    uuid primary key default gen_random_uuid(),
  requirement_result_id uuid not null references app.requirement_results (id) on delete cascade,
  resume_fragment_id    uuid not null references app.resume_fragments (id) deferrable initially deferred,
  citation              text
);

-- -------------------------------------------------------------------------------------
-- Interviews (bonus: AI interview agent)
-- -------------------------------------------------------------------------------------
create table app.interviews (
  id              uuid primary key default gen_random_uuid(),
  evaluation_id   uuid not null references app.evaluations (id) on delete cascade,
  interviewer_id  uuid references app.interviewers (user_id) on delete set null, -- null when the AI agent conducts it
  status          text not null default 'SCHEDULED'
                  check (status in ('SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED')),
  ai_model        text,
  prompt_version  text,
  inconsistencies jsonb,
  recommendation  text check (recommendation in ('ADVANCE', 'DO_NOT_ADVANCE', 'REVIEW')),
  created_at      timestamptz not null default now(),
  completed_at    timestamptz
);

create table app.interview_turns (
  id                uuid primary key default gen_random_uuid(),
  interview_id      uuid not null references app.interviews (id) on delete cascade,
  turn_order        int not null check (turn_order > 0),
  question          text not null,
  answer            text,
  answer_evaluation text,
  score             numeric check (score between 0 and 100),
  confidence        numeric check (confidence between 0 and 1),
  unique (interview_id, turn_order)
);

-- -------------------------------------------------------------------------------------
-- updated_at triggers
-- -------------------------------------------------------------------------------------
create trigger job_openings_set_updated_at before update on app.job_openings
  for each row execute function app.set_updated_at();
create trigger applications_set_updated_at before update on app.applications
  for each row execute function app.set_updated_at();

-- -------------------------------------------------------------------------------------
-- Indexes: foreign keys not already covered by a PK/UNIQUE leading column
-- -------------------------------------------------------------------------------------
create index interviewers_company_idx          on app.interviewers (company_id);
create index job_openings_company_idx          on app.job_openings (company_id);
create index job_openings_creator_idx          on app.job_openings (creator_id);
create index requirements_role_profile_idx     on app.requirements (role_profile_id);
create index applications_candidate_idx        on app.applications (candidate_id);
create index evaluations_application_idx       on app.evaluations (application_id);
create index evaluations_resume_idx            on app.evaluations (resume_id);
create index evaluations_ranking_idx           on app.evaluations (role_profile_id, total_score desc);
create index requirement_results_req_idx       on app.requirement_results (requirement_id);
create index evidence_requirement_result_idx   on app.evidence (requirement_result_id);
create index evidence_resume_fragment_idx      on app.evidence (resume_fragment_id);
create index interviews_evaluation_idx         on app.interviews (evaluation_id);
create index interviews_interviewer_idx        on app.interviews (interviewer_id);

-- Vector search (RAG). Requirements are few per profile: no ANN index needed there.
create index resume_fragments_embedding_hnsw_idx
  on app.resume_fragments using hnsw (embedding extensions.vector_cosine_ops);

-- -------------------------------------------------------------------------------------
-- Row Level Security (defense in depth)
-- Table grants for app_api/app_engine come from the default privileges in the baseline.
-- anon/authenticated have no grants and no policies -> no access even if exposed by mistake.
-- RLS is enabled but not forced, so the owner (postgres, used for migrations) is unaffected.
-- Per-company authorization is enforced by the .NET API (company_id of the recruiter).
-- -------------------------------------------------------------------------------------
do $$
declare
  t text;
begin
  foreach t in array array[
    'companies', 'interviewers', 'candidates', 'job_openings', 'role_profiles', 'requirements',
    'resumes', 'resume_fragments', 'applications', 'evaluations', 'requirement_results',
    'evidence', 'interviews', 'interview_turns'
  ]
  loop
    execute format('alter table app.%I enable row level security', t);
    execute format(
      'create policy service_access on app.%I for all to app_api, app_engine using (true) with check (true)',
      t);
  end loop;
end
$$;
