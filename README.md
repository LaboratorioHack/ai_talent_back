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
`supabase/config.toml` define Auth (MFA, entrada y registro con enlace mágico de Supabase), Storage (bucket `cvs` privado) y la exposición de la API.

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

1. La persona escribe su correo en `/login`; Supabase Auth (`signInWithOtp`) crea la cuenta si es nueva y le manda el enlace.
2. Abre el enlace: `/auth/callback` canjea el código y deja la sesión.
3. `GET /api/auth/me` devuelve `company: null`, así que la app pide crear o unirse a una empresa.
4. `POST /api/companies` con `{ companyName, sector, size, yourName }` la crea y la persona queda como **admin**.
5. Para usar acciones de admin, debe activar MFA (TOTP) en Supabase. Sin MFA, la API responde 403.

### Flujo: sumar personal con código de invitación

1. El admin, con MFA, pide un código: `POST /api/companies/current/invite-codes`.
2. Comparte el código con la persona (la app no lo envía).
3. La persona entra con el enlace de Supabase y llama a `POST /api/companies/join` con `{ code, yourName }`; queda como **recruiter**.
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
| POST | `/api/role-profiles/propose` | La IA propone de 3 a 8 requisitos (tipo, peso y equivalencias) a partir de la descripción del cargo. Body: `{ description, title?, area?, existing? }`; `existing` son los requisitos ya escritos: no se repiten y los pesos nuevos completan hasta 100. No guarda nada. |
| POST | `/api/role-profiles/equivalences` | La IA sugiere hasta 4 equivalencias (`label`, `percent`) para un requisito. Body: `{ requirement, title?, description?, existing? }`. No guarda nada. |
| GET | `/api/vacancies/{id}/public` | **Público (sin login).** Página de una vacante abierta: título, área, cierre, plazas, empresa, descripción y requisitos (`type`, `category`, `description`, `condition`). No expone pesos ni equivalencias. Responde 404 si no está `OPEN`, venció el cierre, fue eliminada o no existe. |
| GET | `/api/vacancies/{id}/applications` | Lista las postulaciones recibidas para la vacante (candidato, estado de selección y estado/procesamiento del CV más reciente). Requiere miembro activo de la empresa. |
| PATCH | `/api/vacancies/{id}/applications/{applicationId}` | Mueve una postulación por el pipeline: body `{ "status": "SHORTLISTED", "reason": "opcional" }`. Flujo `APPLIED → SHORTLISTED → INTERVIEW → HIRED`; `REJECTED` o `WITHDRAWN` desde cualquier etapa abierta. `REJECTED`, `HIRED` y `WITHDRAWN` son terminales. Movimientos inválidos o a la misma etapa responden 409. Queda registrado en auditoría. Requiere miembro activo de la empresa. |
| POST | `/api/vacancies/{id}/applications` | **Público (sin login).** Postulación de un candidato con un solo CV (`multipart/form-data`: `file` PDF/DOCX ≤ 10 MB, `name`, `email`, `consent=true`). Solo vacantes `OPEN` y antes del cierre; volver a postular a la misma vacante se rechaza. No devuelve puntajes ni ranking. |

Cada requisito lleva `type` (`MANDATORY`, `DESIRABLE`, `DISQUALIFYING`), `weight` (0–100) y `equivalences` como porcentajes, por ejemplo `{ "AWS": 80 }` para "AWS cuenta 80% de Azure".
Los conteos de la lista (`resumes`, `meets`, `partiallyMeets`, `doesNotMeet`, `disqualified`, `interviews`) se devuelven en cero hasta que existan la carga de CVs y las evaluaciones.

### Entrada y registro (Supabase Auth, sin contraseña)

Todo lo hace Supabase Auth: el front llama a `signInWithOtp` con el correo, Supabase crea la cuenta la primera vez y **manda el correo con el enlace**. El enlace vuelve a `/auth/callback` de la web, que canjea el código y deja la sesión. La API no participa: solo valida el JWT.

El endpoint `POST /api/public/portal-link` (enlace enviado por el SMTP de Criba) está comentado. El SMTP de Criba (`Smtp__*`) se sigue usando para las invitaciones a entrevista y demás avisos, no para entrar.

Configuración del proyecto hospedado (Dashboard de Supabase; `supabase db push` no la aplica):

1. **Authentication → Sign In / Providers → Email**: activado, y **Allow new users to sign up** encendido.
2. **Authentication → URL Configuration**: Site URL `https://app.criba.isroot.in`; Redirect URLs `https://app.criba.isroot.in/auth/callback` y `http://localhost:3000/auth/callback`.
3. **Authentication → Emails → SMTP Settings**: configurar un SMTP propio. El de Supabase por defecto solo envía a los miembros del equipo del proyecto y unos pocos correos por hora.
4. **Authentication → Rate Limits**: subir *emails sent per hour* tras configurar el SMTP.

#### Google y Microsoft

Los botones del login y del portal usan `signInWithOAuth` (`google` y `azure`). La primera vez crean la cuenta, igual que el enlace. En ambos proveedores la URL de redirección autorizada es `https://<project-ref>.supabase.co/auth/v1/callback`.

- **Google**: Google Cloud Console → APIs & Services → Credentials → *OAuth client ID* (Web application). Agregar esa URL en *Authorized redirect URIs*. Copiar Client ID y Secret en Supabase → Authentication → Sign In / Providers → **Google**.
- **Microsoft**: Microsoft Entra ID → App registrations → New registration, con *Accounts in any organizational directory and personal Microsoft accounts*. Agregar esa URL como Redirect URI (Web), crear un *client secret* y copiar Application (client) ID y el valor del secret en Supabase → **Azure**, con Azure Tenant URL `https://login.microsoftonline.com/common`. En *Token configuration* agregar el claim opcional `email`.

En el portal, el postulante debe entrar con la misma cuenta de correo con la que se postuló: el portal busca sus postulaciones por correo.

### Configuración necesaria

| Variable | Dónde | Notas |
|---|---|---|
| `SUPABASE_URL` | API | URL del proyecto Supabase |
| `INVITES__SIGNINGKEY` | API | Mínimo 32 caracteres. Generar con `openssl rand -base64 48`. |
| `SUPABASE_SERVICE_ROLE_KEY` | Ops / AI Engine | Storage y `scripts/provision-user.sh` (opcional). Nunca en el navegador. |

La API no arranca si falta `SUPABASE_URL` (fuera de Development) o `INVITES__SIGNINGKEY`.

### Email (SMTP)

La API envía correos con `IEmailSender` (`SmtpEmailSender`, MailKit). Por defecto usa Gmail (`smtp.gmail.com:587`, STARTTLS) con la cuenta `cribaai984@gmail.com`.

| Variable | Notas |
|---|---|
| `Smtp__Username` / `Smtp__FromAddress` | Cuenta que envía. `FromAddress` usa `Username` si está vacío. |
| `Smtp__Password` | **App Password** de Gmail (requiere verificación en 2 pasos): https://myaccount.google.com/apppasswords. Nunca la contraseña de la cuenta. |
| `Smtp__Host` / `Smtp__Port` / `Smtp__FromName` | Opcionales: `smtp.gmail.com`, `587`, `Criba AI`. |

Si faltan credenciales la API arranca igual, pero `SendAsync` lanza `InvalidOperationException`.

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

### Seed de CVs (Kaggle + randomuser.me)

Parea cada candidato sintético de [randomuser.me](https://randomuser.me/) con un CV del [dataset de Snehaan Bhawal](https://www.kaggle.com/datasets/snehaanbhawal/resume-dataset) (PDF + `Resume_str`), sube el archivo a R2/Supabase y corre el pipeline RAG:

```bash
uv run seed-resumes --limit 25
uv run seed-resumes --limit 10 --category INFORMATION-TECHNOLOGY
```

La primera corrida descarga el dataset con `kagglehub` (cache local). Flags útiles: `--skip-storage`, `--force`, `--dataset-dir`, `--concurrency`. Equivalente: `make seed-resumes`.

| Servicio | URL | Docs (Scalar, solo Development) |
|---|---|---|
| AI Engine | http://localhost:8000 | http://localhost:8000/scalar |
| API .NET | http://localhost:5022 | http://localhost:5022/scalar |

Para probar rutas `/v1/*` del engine en Scalar, configura el header `X-Internal-Api-Key` con el mismo valor que `ENGINE_API_KEY` / `AiEngine__ApiKey`.

Solo el engine (sin API):

```bash
cd src/ai_engine && uv run uvicorn ai_engine.api.main:app --reload --port 8000
```
