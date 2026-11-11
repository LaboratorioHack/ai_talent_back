#!/usr/bin/env bash
# Run API .NET + AI Engine (RAG) locally with ai_talent_back/.env.development
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENV_FILE="${ROOT}/.env.development"
ENGINE_PID=""

cleanup() {
  if [[ -n "${ENGINE_PID}" ]] && kill -0 "${ENGINE_PID}" 2>/dev/null; then
    echo "Stopping AI Engine (pid ${ENGINE_PID})..."
    kill "${ENGINE_PID}" 2>/dev/null || true
    wait "${ENGINE_PID}" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Missing ${ENV_FILE}. Copy from .env.example and fill in secrets."
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "${ENV_FILE}"
set +a

export ENVIRONMENT="${ENVIRONMENT:-development}"

echo "==> AI Engine (FastAPI) http://localhost:8000  |  Scalar: http://localhost:8000/scalar"
(
  cd "${ROOT}/src/ai_engine"
  uv sync --quiet
  exec uv run uvicorn ai_engine.api.main:app --host 0.0.0.0 --port 8000 --reload
) &
ENGINE_PID=$!

sleep 2
if ! kill -0 "${ENGINE_PID}" 2>/dev/null; then
  echo "AI Engine failed to start. Check logs above."
  exit 1
fi

echo "==> API .NET http://localhost:5022  |  Scalar: http://localhost:5022/scalar"
cd "${ROOT}/src/api"
exec dotnet run --project src/AiTalent.Api/AiTalent.Api.csproj --launch-profile http
