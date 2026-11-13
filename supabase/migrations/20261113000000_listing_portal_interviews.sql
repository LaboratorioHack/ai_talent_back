-- Data the web app showed from mocks until now (no more demo data in the front):
--   * Public listing of a vacancy: location, modality, contract, schedule, salary, publish date.
--   * File details of each CV (name, type, size) for the recruiter's list and the applicant portal.
--   * Candidate AI interview by link: a random token, its expiry and the invitation language.

alter table app.job_openings
  add column location      text,
  add column modality      text,
  add column contract_type text,
  add column schedule      text,
  add column salary        text,
  add column published_at  timestamptz;

alter table app.job_openings
  add constraint job_openings_location_check check (location is null or char_length(location) between 2 and 80),
  add constraint job_openings_modality_check check (modality is null or modality in ('ONSITE', 'HYBRID', 'REMOTE')),
  add constraint job_openings_contract_check check (contract_type is null or char_length(contract_type) between 2 and 80),
  add constraint job_openings_schedule_check check (schedule is null or char_length(schedule) between 2 and 80),
  add constraint job_openings_salary_check check (salary is null or char_length(salary) between 1 and 80);

-- Open vacancies published before this migration get their last update as publish date.
update app.job_openings set published_at = updated_at where status <> 'DRAFT' and published_at is null;

-- Public board: every open vacancy of every company.
create index job_openings_public_idx on app.job_openings (published_at desc)
  where status = 'OPEN' and deleted_at is null;

alter table app.resumes
  add column file_name    text,
  add column content_type text,
  add column size_bytes   bigint check (size_bytes is null or size_bytes >= 0);

-- The token is the only secret of the candidate's interview link: never logged, compared as is.
alter table app.interviews
  add column token      text,
  add column expires_at timestamptz,
  add column language   text not null default 'es' check (language in ('es', 'es-en', 'en'));

create unique index interviews_token_idx on app.interviews (token) where token is not null;

-- The applicant portal finds a person's applications by the e-mail of their magic-link session.
create index candidates_contact_email_idx on app.candidates (lower(contact_email));

comment on column app.job_openings.modality is 'ONSITE, HYBRID or REMOTE; null when the company did not say.';
comment on column app.job_openings.published_at is 'First time the vacancy was published (OPEN).';
comment on column app.interviews.token is 'Random secret of the candidate interview link.';
