#!/usr/bin/env bash
# Create a user in Supabase Auth while public sign-up is disabled (closed registration).
#
# Usage:
#   SUPABASE_URL=... SUPABASE_SERVICE_ROLE_KEY=... ./scripts/provision-user.sh <email> <name> [password]
#
# The service role key is required and must never be exposed to the browser or the API.
# This only creates the Auth user: the company and role are created later by the user
# through POST /api/companies (admin) or POST /api/companies/join (invited recruiter).
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: provision-user.sh <email> <name> [password]

Environment:
  SUPABASE_URL               Base URL of the Supabase project (required)
  SUPABASE_SERVICE_ROLE_KEY  Service role key (required, server-side only)

If password is omitted, a random one is generated and printed.
EOF
}

if [[ $# -lt 2 || "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 1
fi

EMAIL="$1"
NAME="$2"

: "${SUPABASE_URL:?Set SUPABASE_URL}"
: "${SUPABASE_SERVICE_ROLE_KEY:?Set SUPABASE_SERVICE_ROLE_KEY (never commit or expose it)}"

SUPABASE_URL="${SUPABASE_URL%/}"

if [[ -n "${3:-}" ]]; then
  PASSWORD="$3"
  GENERATED_PASSWORD=0
else
  # 32 hex characters, safe to paste into a password field.
  PASSWORD="$(openssl rand -hex 16)"
  GENERATED_PASSWORD=1
fi

json_escape() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

BODY="$(printf '{"email":"%s","password":"%s","email_confirm":true,"user_metadata":{"name":"%s"}}' \
  "$(json_escape "${EMAIL}")" "$(json_escape "${PASSWORD}")" "$(json_escape "${NAME}")")"

RESPONSE_FILE="$(mktemp)"
trap 'rm -f "${RESPONSE_FILE}"' EXIT

STATUS="$(curl -sS -o "${RESPONSE_FILE}" -w '%{http_code}' \
  -X POST "${SUPABASE_URL}/auth/v1/admin/users" \
  -H "apikey: ${SUPABASE_SERVICE_ROLE_KEY}" \
  -H "Authorization: Bearer ${SUPABASE_SERVICE_ROLE_KEY}" \
  -H "Content-Type: application/json" \
  --data "${BODY}")"

if [[ "${STATUS}" != "200" && "${STATUS}" != "201" ]]; then
  echo "Failed to create ${EMAIL} (HTTP ${STATUS}):" >&2
  cat "${RESPONSE_FILE}" >&2
  echo >&2
  exit 1
fi

echo "Created Supabase user: ${EMAIL}"
if [[ "${GENERATED_PASSWORD}" == "1" ]]; then
  echo "Temporary password: ${PASSWORD}"
  echo "Share it through a private channel; the user can change it with the password reset flow."
fi
