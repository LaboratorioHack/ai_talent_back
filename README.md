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

## Autenticación y onboarding de empresas

El inicio de sesión lo maneja **Supabase Auth**. La API solo verifica el token que emite Supabase (firma, emisor, audiencia y expiración) y decide qué puede hacer cada usuario según su empresa y rol.

### Endpoints

| Método | Ruta | Acceso | Descripción |
|---|---|---|---|
| GET | `/api/auth/me` | Usuario autenticado | Devuelve `userId`, `email`, `name`, si completó MFA y su empresa (`id`, `name`, `sector`, `size`, `role`). `name` y `company` son `null` hasta que la persona crea o se une a una empresa. |
| POST | `/api/companies` | Sin empresa aún | Crea la empresa y hace admin al usuario. Body: `{ companyName, sector, size, yourName }`. `size` debe ser `1-10`, `11-50`, `51-200`, `201-500`, `501-1000` o `1000+`. |
| POST | `/api/companies/current/invite-codes` | Admin con MFA | Genera un código de invitación válido 7 días. |
| POST | `/api/companies/join` | Sin empresa aún (10 intentos / 15 min) | Entra a la empresa con un código, como recruiter. Body: `{ code, yourName }`. |
| GET | `/api/staff` | Admin con MFA | Lista el personal de la empresa. |
| PATCH | `/api/staff/{userId}` | Admin con MFA | Cambia rol o activa/desactiva. Un admin no puede cambiar a otro admin ni a sí mismo. |

Documentación interactiva: `http://localhost:5022/scalar/v1` (solo en Development).

### Flujo: alta de una empresa nueva

1. Ops crea la cuenta con `scripts/provision-user.sh` (el registro público está cerrado).
2. La persona inicia sesión en Supabase y obtiene un token.
3. `GET /api/auth/me` devuelve `company: null`, así que la app pide crear o unirse a una empresa.
4. `POST /api/companies` con `{ companyName, sector, size, yourName }` la crea y la persona queda como **admin**.
5. Para usar acciones de admin, debe activar MFA (TOTP) en Supabase. Sin MFA, la API responde 403.

### Flujo: sumar personal con código de invitación

1. El admin, con MFA, pide un código: `POST /api/companies/current/invite-codes`.
2. Comparte el código con la persona (la app no lo envía).
3. La persona hace login (su cuenta ya fue creada por ops) y llama a `POST /api/companies/join` con `{ code, yourName }`; queda como **recruiter**.
4. Si hace falta un admin nuevo, otro admin lo promueve con `PATCH /api/staff/{userId}`.

### Reglas del código de invitación

- Firmado con HMAC y la clave `INVITES__SIGNINGKEY`. Expira a los 7 días.
- Cualquiera que tenga el código puede usarlo hasta que expire. No se puede revocar uno solo.
- Un código alterado, vencido o inexistente responde siempre el mismo 400.
- Quien ya pertenece a una empresa no puede unirse a otra (409).
- Si entra la persona equivocada, un admin la desactiva con `PATCH` y pierde acceso de inmediato.

### Vacantes y perfil de cargo

Cualquier miembro activo (ADMIN o RECRUITER) puede gestionar las vacantes de su empresa; no hace falta MFA.
Cada vacante tiene un perfil de cargo versionado con sus requisitos. El perfil se puede editar mientras no se haya usado en una evaluación; después, cada cambio crea una versión nueva para que los puntajes sigan siendo auditables.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/vacancies?status=&q=&page=&pageSize=` | Lista las vacantes de la empresa, más recientes primero. `status`: `DRAFT`, `OPEN` o `CLOSED`; `q` busca en título y área. |
| POST | `/api/vacancies` | Crea un borrador con su perfil y requisitos en una sola llamada. Body: `{ title, area, closingAt, positions, description, requirements }`. |
| GET | `/api/vacancies/{id}` | Devuelve la vacante con su perfil actual, requisitos y suma de pesos. |
| PATCH | `/api/vacancies/{id}` | Actualiza título, área, fecha de cierre y número de plazas. |
| POST | `/api/vacancies/{id}/publish` | Pasa de `DRAFT` a `OPEN`. Exige al menos un requisito y pesos que sumen 100. |
| POST | `/api/vacancies/{id}/close` | Pasa de `OPEN` a `CLOSED`. |
| DELETE | `/api/vacancies/{id}` | Borrado lógico: deja de aparecer y responde 404, pero conserva postulaciones y auditoría. |
| PUT | `/api/vacancies/{id}/profile` | Reemplaza descripción y requisitos. Crea versión nueva si el perfil ya se usó. |

Cada requisito lleva `type` (`MANDATORY`, `DESIRABLE`, `DISQUALIFYING`), `weight` (0–100) y `equivalences` como porcentajes, por ejemplo `{ "AWS": 80 }` para "AWS cuenta 80% de Azure".
Los conteos de la lista (`resumes`, `meets`, `partiallyMeets`, `doesNotMeet`, `disqualified`, `interviews`) se devuelven en cero hasta que existan la carga de CVs y las evaluaciones.

### Alta de usuarios (registro cerrado)

No hay registro público: `supabase/config.toml` define `enable_signup = false` y la API no expone `/api/auth/signup`. El login, el refresh de token y la recuperación de contraseña siguen funcionando en Supabase Auth.

Las cuentas se crean desde ops, nunca desde el navegador:

```bash
export SUPABASE_URL="https://<project-ref>.supabase.co"
export SUPABASE_SERVICE_ROLE_KEY="<service-role-key>"   # nunca en el front ni en la API
./scripts/provision-user.sh persona@empresa.com "Nombre Persona"
```

El script usa `POST /auth/v1/admin/users` con `email_confirm: true`, así que la persona puede iniciar sesión de inmediato. Solo crea el usuario de Auth: la empresa y el rol se crean después con `POST /api/companies` (o `POST /api/companies/join`).

También se puede hacer a mano en Supabase Studio → Authentication → Users → Add user (marcando *Auto Confirm User*).

En el proyecto hospedado hay que desactivar también **Authentication → Sign In / Providers → Allow new users to sign up**. `supabase db push` no aplica esta configuración; si solo se cambia `config.toml`, el proyecto remoto sigue permitiendo registros.

### Configuración necesaria

| Variable | Dónde | Notas |
|---|---|---|
| `SUPABASE_URL` | API | URL del proyecto Supabase |
| `INVITES__SIGNINGKEY` | API | Mínimo 32 caracteres. Generar con `openssl rand -base64 48`. |
| `SUPABASE_SERVICE_ROLE_KEY` | Ops / AI Engine | Solo para `scripts/provision-user.sh` y Storage. Nunca en la API ni en el navegador. |

La API no arranca si falta `SUPABASE_URL` (fuera de Development) o `INVITES__SIGNINGKEY`.

## CI/CD (sin redundancia)

| Evento | Workflow | Hace | No hace |
|---|---|---|---|
| `pull_request` → main | `ci.yml` | format/lint, build, tests y auditoría de dependencias (solo del servicio modificado) | imágenes, deploy |
| `push` → main (merge) | `cd.yml` | migraciones Supabase, build+push imagen a GHCR, deploy EC2 (solo servicios modificados) | lint/tests (ya validados en el PR) |

Para que esto sea seguro: **branch protection en `main`** exigiendo el check `ci-ok` y prohibiendo push directo.

### Secrets de GitHub requeridos
`EC2_HOST`, `EC2_USER`, `EC2_SSH_KEY`, `SUPABASE_ACCESS_TOKEN`, `SUPABASE_DB_PASSWORD`, `SUPABASE_PROJECT_ID`. Environment `production`.

### EC2 (una vez)
Docker + compose plugin, `~/ai-talent/.env` (ver `.env.example`), puertos 80/443 abiertos. Caddy emite TLS automáticamente para `DOMAIN`.

## Desarrollo local (API + AI Engine / RAG)

1. Copia y completa [`/.env.example`](.env.example) como **`.env.development`** en la raíz de `ai_talent_back/` (ya está en `.gitignore`).
2. Requisitos: [.NET 10 SDK](https://dotnet.microsoft.com/download), [uv](https://docs.astral.sh/uv/), acceso a Supabase y keys de Anthropic / OpenRouter.

Levanta **AI Engine** (puerto 8000) y **API .NET** (puerto 5022) en un solo comando (desde la raíz de `ai_talent_back/`):

```bash
cd ai_talent_back
uv sync
uv run dev
```

Atajos equivalentes: `make dev` · tests del engine: `uv run test-engine` o `make test-engine`

| Servicio | URL | Docs (Scalar, solo Development) |
|---|---|---|
| AI Engine | http://localhost:8000 | http://localhost:8000/scalar |
| API .NET | http://localhost:5022 | http://localhost:5022/scalar |

Para probar rutas `/v1/*` del engine en Scalar, configura el header `X-Internal-Api-Key` con el mismo valor que `ENGINE_API_KEY` / `AiEngine__ApiKey`.

Solo el engine (sin API):

```bash
cd src/ai_engine && uv run uvicorn ai_engine.api.main:app --reload --port 8000
```
