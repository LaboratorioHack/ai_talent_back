-- Who sent each application, when they were signed in. A person may apply with one e-mail
-- (e.g. a personal one) and sign in to the portal with another (e.g. a university account): the
-- portal then lists the applications of the session e-mail plus the ones that person sent.
-- Kept on the application, not on the candidate, so signing in never exposes someone else's data.

alter table app.applications add column if not exists submitted_by uuid;

create index if not exists applications_submitted_by_idx
  on app.applications (submitted_by)
  where submitted_by is not null;

comment on column app.applications.submitted_by is
  'Supabase Auth user who submitted the application while signed in (null for anonymous applications).';
