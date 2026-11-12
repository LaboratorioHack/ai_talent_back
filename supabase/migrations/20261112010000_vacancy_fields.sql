-- Vacancy screen fields (MVP 4.1): area, closing date, number of positions.
-- Soft delete keeps applications, evaluations and audit history intact once they exist.

alter table app.job_openings
  add column area text,
  add column closing_at timestamptz,
  add column positions int not null default 1,
  add column deleted_at timestamptz;

alter table app.job_openings
  add constraint job_openings_area_check
  check (area is null or char_length(area) between 2 and 80);

alter table app.job_openings
  add constraint job_openings_positions_check
  check (positions >= 1);

create index job_openings_company_status_idx
  on app.job_openings (company_id, status)
  where deleted_at is null;

-- Equivalences are stored per requirement, e.g. {"Azure": 80} means Azure counts 80%.
alter table app.requirements
  add column equivalences jsonb not null default '{}'::jsonb;

alter table app.requirements
  add constraint requirements_equivalences_object_check
  check (jsonb_typeof(equivalences) = 'object');
