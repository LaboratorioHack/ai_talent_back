-- Interview UI fields expected by the web app (started time, turn topic).

alter table app.interviews
  add column if not exists started_at timestamptz;

alter table app.interview_turns
  add column if not exists topic text;

comment on column app.interviews.started_at is 'When the live interview started; falls back to created_at in the API when null.';
comment on column app.interview_turns.topic is 'Short label for the question block (e.g. Experiencia, Inglés).';
