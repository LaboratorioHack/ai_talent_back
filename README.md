# AI Talent Scout — Backend

Evaluación automática de hojas de vida contra un perfil de cargo, con ranking explicado y entrevista asistida por agente.

## Arquitectura

```
                 ┌──────────────┐         ┌───────────────────────────┐
  ai_talent_web ─►  API .NET 10  ├── HTTP ─►  AI Engine (Python/FastAPI) │
   (Next.js)     │  (gateway,   │         │  ingesta · anonimización  │
                 │  negocio,    │         │  extracción · RAG ·       │
                 │  entrevista) │         │  agentes de evaluación    │
                 └──────┬───────┘         └────────────┬──────────────┘
                        │        Supabase (Postgres + pgvector + Storage + Auth)
                        └──────────────────────┬───────┘
                                               ▼
                                       Anthropic (Claude)
```

| Servicio | Responsabilidad | Microsoft Agent Framework |
|---|---|---|
| `src/api` (.NET 10) | API pública, vacantes, candidatos, rankings, auditoría, auth (JWT Supabase). Orquesta al AI Engine. | **Agente de entrevista** (bonus): preguntas dinámicas, detección de inconsistencias, recomendación. Streaming al front. |
| `src/ai_engine` (Python) | Parseo PDF/DOCX, **anonimización** (IA responsable), extracción estructurada, embeddings, RAG, scoring y explicación. Solo red interna. | **Agentes evaluador y explicador** (workflow por candidato). |

### API .NET — Clean Architecture + Vertical Slices
```
src/api/src/
  AiTalent.Domain/          Entidades, value objects, enums (Cumple/Parcial/NoCumple), errores. Sin dependencias.
  AiTalent.Application/     Casos de uso agrupados por feature (Features/Vacancies, Candidates, Evaluations, Rankings, Interviews).
  AiTalent.Infrastructure/  EF Core + Npgsql (Supabase), cliente HTTP al AI Engine, Storage, Agents (MAF).
  AiTalent.Api/             Minimal APIs (Endpoints por feature), middleware, DI.
src/api/tests/              Unit, Integration, Architecture (reglas de dependencias entre capas).
```

### AI Engine — pipeline
`ingestion/parsers` → `anonymization` (quita edad, género, foto, estado civil, dirección, nacionalidad **antes** de que el LLM vea el CV) → `extraction` (perfil estructurado) → `rag` (taxonomía de skills/equivalencias: AWS≠Azure) → `agents/evaluator` + `scoring` (obligatorios / deseables / descalificantes, confianza) → `agents/explainer`.
`evals/` contiene sets de prueba para medir calidad del ranking y sesgo.

### Base de datos
`supabase/migrations` (Supabase CLI) es la **única fuente de verdad** del esquema (lo usan .NET y Python). EF Core se usa sin migraciones.
Los datos viven en el esquema privado `app` (no expuesto por la Data API); el front solo usa Supabase Auth.
`supabase/config.toml` define Auth (MFA, sin registro público), Storage (bucket `cvs` privado) y la exposición de la API.

## Seguridad
Ver [SECURITY.md](SECURITY.md): mapeo OWASP Top 10:2025 + OWASP LLM Top 10 y checklist de despliegue.

## CI/CD (sin redundancia)

| Evento | Workflow | Hace | No hace |
|---|---|---|---|
| `pull_request` → main | `ci.yml` | format/lint, build, tests y auditoría de dependencias (solo del servicio modificado) | imágenes, deploy |
| `push` → main (merge) | `cd.yml` | migraciones Supabase, build+push imagen a GHCR, deploy EC2 (solo servicios modificados) | lint/tests (ya validados en el PR) |

Para que esto sea seguro: **branch protection en `main`** exigiendo el check `ci-ok` y prohibiendo push directo.

### Secrets de GitHub requeridos
`EC2_HOST`, `EC2_USER`, `EC2_SSH_KEY`, `SUPABASE_ACCESS_TOKEN`, `SUPABASE_DB_PASSWORD`, `SUPABASE_PROJECT_ID`. Environment `production`.

### EC2 (una vez)
Docker + compose plugin, `/opt/ai-talent/.env` (ver `.env.example`), puertos 80/443 abiertos. Caddy emite TLS automáticamente para `DOMAIN`.
