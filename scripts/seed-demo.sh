#!/usr/bin/env bash
# Load the fictional demo organization (Cordillera Financiera) into a database.
#
# Usage:
#   DATABASE_URL=postgresql://... ./scripts/seed-demo.sh [--admin <email>] [--applicant <email>]
#
#   --admin      An existing Supabase Auth user (see provision-user.sh) that becomes ADMIN of the demo
#                organization. Skipped if that user already belongs to another company.
#   --applicant  E-mail that takes over the demo applicant "Sebastián Vega", so a magic-link
#                session with it sees the applicant portal with real applications.
#
# Re-running it replaces the demo rows (fixed ids) and keeps everything else. `supabase db reset`
# loads the same data through supabase/seed/demo.sql.
set -euo pipefail

: "${DATABASE_URL:?Set DATABASE_URL (Session pooler connection string)}"

ADMIN=""
APPLICANT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --admin) ADMIN="$2"; shift 2 ;;
    --applicant) APPLICANT="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PGCLIENTENCODING=UTF8
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -q -f "$ROOT/supabase/seed/demo.sql"
echo "Demo data loaded."

if [[ -n "$ADMIN" ]]; then
  psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -q -v email="$ADMIN" <<'SQL'
insert into app.interviewers (user_id, company_id, name, role)
select u.id, c.id, coalesce(u.raw_user_meta_data ->> 'name', split_part(u.email, '@', 1)), 'ADMIN'
from auth.users u
cross join app.companies c
where lower(u.email) = lower(:'email')
  and c.name = 'Cordillera Financiera'
  and not exists (select 1 from app.interviewers i where i.user_id = u.id);

-- Human decisions of the demo, attributed to the admin so governance shows real names.
insert into app.audit_log (actor_id, actor_type, action, entity, entity_id, details, occurred_at)
select i.user_id, 'user', d.action, d.entity, d.entity_id, d.details::jsonb, now() - d.ago
from app.interviewers i
join auth.users u on u.id = i.user_id and lower(u.email) = lower(:'email')
join app.companies c on c.id = i.company_id and c.name = 'Cordillera Financiera'
cross join (values
  ('vacancy.published', 'job_opening', 'cloud', '{"title": "Ingeniero Cloud Senior"}', interval '24 days'),
  ('vacancy.published', 'job_opening', 'data', '{"title": "Analista de Datos"}', interval '20 days'),
  ('application.interview.invited', 'application', 'cloud-1', '{"summary": "Invitó a entrevista con IA", "candidate": "Daniela Osorio"}', interval '15 days'),
  ('application.status.changed', 'application', 'cloud-8', '{"summary": "Descartó por no cumplir un obligatorio", "candidate": "Ricardo Méndez"}', interval '9 days'),
  ('evaluation.ai_error.flagged', 'evaluation', 'cloud-3', '{"summary": "Marcó un error de la IA: No vio algo que sí está en la hoja de vida", "candidate": "Julián Ríos"}', interval '7 days'),
  ('application.status.changed', 'application', 'data-5', '{"summary": "Eligió a la persona para el cargo", "candidate": "Natalia Franco"}', interval '3 days')
) as d(action, entity, entity_id, details, ago)
where not exists (
  select 1 from app.audit_log a
  where a.actor_id = i.user_id and a.action = d.action and a.entity_id = d.entity_id
);
SQL
  echo "Admin linked: $ADMIN"
fi

if [[ -n "$APPLICANT" ]]; then
  psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -q -v email="$APPLICANT" <<'SQL'
update app.candidates
set contact_email = lower(:'email')
where name = 'Sebastián Vega'
  and company_id in (select id from app.companies where name in ('Cordillera Financiera', 'Pagos Cóndor', 'Andina Logística'));
SQL
  echo "Demo applicant now answers to: $APPLICANT"
fi
