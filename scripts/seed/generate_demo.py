"""Generates supabase/seed/demo.sql: a fictional demo organization with real-looking data.

    python scripts/seed/generate_demo.py > supabase/seed/demo.sql

Everything is fictional (companies, people, CVs). Ids are derived from stable keys
(md5 -> uuid), so the SQL is idempotent: it deletes the demo rows it owns and inserts
them again. Dates are relative to now() at the time the seed runs, so the dashboard
always has activity in the last 7, 30 and 90 days.

Evaluations follow the same rules as the AI Engine output stored by the API: one
requirement_result per requirement, evidence pointing to a resume fragment, total score
as the weighted sum, fulfillment MEETS only when every mandatory requirement is met.
"""

from __future__ import annotations

import hashlib
import json
import random
import sys

random.seed(20261113)


def uid(key: str) -> str:
    h = hashlib.md5(f"criba-demo:{key}".encode()).hexdigest()
    return f"{h[:8]}-{h[8:12]}-{h[12:16]}-{h[16:20]}-{h[20:]}"


def q(value) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


def jq(value) -> str:
    return q(json.dumps(value, ensure_ascii=False)) + "::jsonb"


def ago(days: float) -> str:
    return f"now() - interval '{days * 24:.2f} hours'"


def ahead(days: float) -> str:
    return f"now() + interval '{days:.0f} days'"


# ---------------------------------------------------------------------------------------------
# Companies and vacancies
# ---------------------------------------------------------------------------------------------

COMPANIES = [
    ("cordillera", "Cordillera Financiera", "Banca y finanzas", "1000+"),
    ("condor", "Pagos Cóndor", "Pagos digitales", "201-500"),
    ("andina", "Andina Logística", "Logística y transporte", "501-1000"),
]

# requirement: key, type, weight, category, description, equivalences, meets phrase, partial phrase
R = {
    "k8s": ("MANDATORY", 25, "Plataforma", "Kubernetes en producción", {"OpenShift": 80},
            "Operé clústeres de AKS en producción durante {y} años, con más de 40 servicios.",
            "Desplegué servicios en Kubernetes en ambientes de pruebas."),
    "azure": ("MANDATORY", 20, "Cloud", "Azure", {"AWS": 80, "GCP": 70},
              "Migré 30 servicios a Azure (App Service, AKS y Key Vault) en diez meses.",
              "Trabajé con AWS: EC2, RDS y S3 para aplicaciones internas."),
    "terraform": ("MANDATORY", 20, "Infraestructura como código", "Terraform", {"Pulumi": 80},
                  "Infraestructura como código con Terraform en tres ambientes.",
                  "Plantillas de ARM y algo de Terraform para redes."),
    "english": ("MANDATORY", 15, "Idiomas", "Inglés B2", {},
                "Inglés B2, reuniones diarias con un equipo en Estados Unidos.",
                "Inglés intermedio, lectura técnica."),
    "cicd": ("DESIRABLE", 10, "Entrega continua", "CI/CD con Azure DevOps o GitHub Actions", {"GitLab CI": 80},
             "Pipelines de despliegue en Azure DevOps y GitHub Actions.",
             "Mantuve algunos pipelines de Jenkins."),
    "monitoring": ("DESIRABLE", 10, "Observabilidad", "Monitoreo con Prometheus o Grafana", {"Datadog": 80},
                   "Monitoreo con Prometheus y Grafana, alertas de disponibilidad.",
                   "Revisaba tableros de Datadog en las guardias."),
    "work_permit": ("DISQUALIFYING", 0, "Legal", "Sin autorización para trabajar en Colombia", {}, None, None),
    # data analyst
    "sql": ("MANDATORY", 30, "Datos", "SQL avanzado", {},
            "Modelos en SQL sobre 200 millones de registros de cartera.",
            "Consultas en SQL para reportes semanales."),
    "powerbi": ("MANDATORY", 20, "Visualización", "Power BI", {"Tableau": 80, "Looker": 80},
                "Doce tableros en Power BI para las áreas de riesgo y cobranza.",
                "Algunos reportes en Excel y un tablero en Looker Studio."),
    "analyst_years": ("MANDATORY", 20, "Experiencia", "3 años o más como analista de datos", {},
                      "{y} años como analista de datos.",
                      "Dos años y medio en análisis; antes, soporte."),
    "english_data": ("MANDATORY", 10, "Idiomas", "Inglés B2", {},
                     "Inglés B2 certificado.",
                     "Inglés intermedio, sin certificado."),
    "finance": ("DESIRABLE", 10, "Sector", "Experiencia en el sector financiero", {"Fintech": 90},
                "Tres años en riesgo de crédito en banca.",
                "Proyectos para una fintech de pagos."),
    "dbt": ("DESIRABLE", 10, "Herramientas", "dbt", {"Dataform": 80},
            "Transformaciones con dbt en BigQuery.",
            "Conozco dbt por un curso."),
    # backend jr
    "dotnet": ("MANDATORY", 35, "Desarrollo", "C# y .NET", {"Java": 70},
               "APIs en .NET 8 con Entity Framework para un sistema de inventario.",
               "Proyectos académicos en Java y Spring."),
    "rest": ("MANDATORY", 25, "Desarrollo", "APIs REST", {},
             "Diseñé y documenté APIs REST con OpenAPI.",
             "Consumí APIs REST desde aplicaciones web."),
    "git": ("MANDATORY", 20, "Herramientas", "Git", {},
            "Git con ramas y pull requests en GitHub.",
            "Git básico en proyectos personales."),
    "sql_jr": ("DESIRABLE", 20, "Datos", "SQL", {},
               "Consultas y procedimientos en SQL Server.",
               "SQL básico en la universidad."),
    # other companies (only their profiles)
    "devops_k8s": ("MANDATORY", 30, "Plataforma", "Kubernetes en producción", {"OpenShift": 80}, None, None),
    "devops_iac": ("MANDATORY", 30, "IaC", "Terraform", {"Pulumi": 80}, None, None),
    "devops_cloud": ("MANDATORY", 25, "Cloud", "AWS o Azure", {"GCP": 70}, None, None),
    "devops_english": ("DESIRABLE", 15, "Idiomas", "Inglés B1 o superior", {}, None, None),
    "pm_roadmap": ("MANDATORY", 35, "Producto", "Gestión de roadmap de producto digital", {}, None, None),
    "pm_metrics": ("MANDATORY", 35, "Producto", "Decisiones con métricas de producto", {}, None, None),
    "pm_agile": ("DESIRABLE", 30, "Métodos", "Equipos ágiles (Scrum o Kanban)", {}, None, None),
    "ops_excel": ("MANDATORY", 40, "Herramientas", "Excel avanzado", {"Google Sheets": 80}, None, None),
    "ops_logistics": ("MANDATORY", 40, "Sector", "Operación logística o de bodegas", {}, None, None),
    "ops_sql": ("DESIRABLE", 20, "Datos", "SQL básico", {}, None, None),
    # draft / closed of the demo org
    "ux_research": ("MANDATORY", 40, "Diseño", "Investigación con usuarios", {}, None, None),
    "ux_figma": ("MANDATORY", 40, "Herramientas", "Figma", {"Sketch": 80}, None, None),
    "ux_proto": ("DESIRABLE", 20, "Diseño", "Prototipos navegables", {}, None, None),
    "sec_siem": ("MANDATORY", 50, "Seguridad", "SIEM", {"Sentinel": 90, "Splunk": 90}, None, None),
    "sec_iso": ("MANDATORY", 50, "Normas", "ISO 27001", {}, None, None),
}

VACANCIES = [
    # key, company, title, area, status, closes_in, positions, location, modality, contract, schedule, salary, published_days_ago, description, requirement keys
    ("cloud", "cordillera", "Ingeniero Cloud Senior", "Tecnología", "OPEN", 18, 2, "Medellín", "HYBRID",
     "Término indefinido", "Tiempo completo", None, 24,
     "Buscamos una persona que lidere la plataforma cloud: migración de servicios a Azure, operación de Kubernetes en producción e infraestructura como código. Trabajará con equipos de desarrollo y seguridad, y con un equipo distribuido en inglés.",
     ["k8s", "azure", "terraform", "english", "cicd", "monitoring", "work_permit"]),
    ("data", "cordillera", "Analista de Datos", "Analítica", "OPEN", 25, 1, "Bogotá", "REMOTE",
     "Término indefinido", "Tiempo completo", "$7 a $9 millones", 20,
     "Buscamos a alguien que convierta datos de operación en decisiones: modelos en SQL, tableros para las áreas de negocio y acompañamiento a quienes los usan.",
     ["sql", "powerbi", "analyst_years", "english_data", "finance", "dbt"]),
    ("backend", "cordillera", "Desarrollador Backend .NET Jr", "Tecnología", "OPEN", 30, 1, "Medellín", "REMOTE",
     "Término fijo, 1 año", "Tiempo completo", None, 9,
     "Te sumarás a un equipo que construye servicios internos. Escribirás APIs, pruebas y documentación, con acompañamiento de personas senior.",
     ["dotnet", "rest", "git", "sql_jr"]),
    ("ux", "cordillera", "Diseñador UX", "Diseño", "DRAFT", 40, 1, "Cali", "HYBRID",
     "Término indefinido", "Tiempo completo", None, None,
     "Diseñarás flujos de producto de punta a punta: investigación con usuarios, prototipos y trabajo diario con desarrollo.",
     ["ux_research", "ux_figma", "ux_proto"]),
    ("sec", "cordillera", "Analista de Seguridad", "Seguridad", "CLOSED", -3, 1, "Bogotá", "ONSITE",
     "Término indefinido", "Tiempo completo", None, 60,
     "Cuidarás la seguridad de la plataforma: gestión de vulnerabilidades, respuesta a incidentes y controles en la nube.",
     ["sec_siem", "sec_iso"]),
    ("devops", "condor", "Ingeniero DevOps", "Tecnología", "OPEN", 21, 2, "Bogotá", "REMOTE",
     "Término indefinido", "Tiempo completo", None, 6,
     "Mantendrás la plataforma de pagos disponible y automatizada: pipelines, Kubernetes en producción e infraestructura como código en la nube.",
     ["devops_k8s", "devops_iac", "devops_cloud", "devops_english"]),
    ("pm", "condor", "Product Manager", "Producto", "OPEN", 15, 1, "Bogotá", "HYBRID",
     "Término indefinido", "Tiempo completo", "$12 a $15 millones", 11,
     "Liderarás un producto digital de pagos: prioridades, roadmap y conversación permanente con clientes y con el equipo de desarrollo.",
     ["pm_roadmap", "pm_metrics", "pm_agile"]),
    ("ops", "andina", "Analista de Operaciones", "Operaciones", "OPEN", 28, 3, "Barranquilla", "ONSITE",
     "Término indefinido", "Tiempo completo", None, 4,
     "Coordinarás indicadores de bodegas y transporte, y propondrás mejoras con datos de la operación.",
     ["ops_excel", "ops_logistics", "ops_sql"]),
]

# ---------------------------------------------------------------------------------------------
# People (fictional). Profile = requirement key -> M (meets), P (partial), N (no evidence), X (does not meet)
# ---------------------------------------------------------------------------------------------

CLOUD_PEOPLE = [
    ("Daniela Osorio", 7, dict(k8s="M", azure="M", terraform="M", english="M", cicd="M", monitoring="M"), "INTERVIEW", 21),
    ("Sebastián Vega", 7, dict(k8s="M", azure="M", terraform="M", english="P", cicd="M", monitoring="M"), "INTERVIEW", 19),
    ("Julián Ríos", 6, dict(k8s="M", azure="P", terraform="M", english="M", cicd="M", monitoring="N"), "SHORTLISTED", 18),
    ("Mariana López", 5, dict(k8s="P", azure="M", terraform="M", english="M", cicd="M", monitoring="M"), "SHORTLISTED", 16),
    ("Andrés Pardo", 8, dict(k8s="M", azure="M", terraform="P", english="M", cicd="N", monitoring="M"), "SHORTLISTED", 15),
    ("Camila Torres", 4, dict(k8s="M", azure="P", terraform="P", english="M", cicd="M", monitoring="N"), "APPLIED", 13),
    ("Laura Quintero", 5, dict(k8s="P", azure="M", terraform="N", english="P", cicd="M", monitoring="M"), "APPLIED", 12),
    ("Ricardo Méndez", 9, dict(k8s="X", azure="M", terraform="M", english="X", cicd="M", monitoring="N"), "REJECTED", 11),
    ("Felipe Arias", 3, dict(k8s="P", azure="P", terraform="N", english="M", cicd="P", monitoring="N"), "APPLIED", 9),
    ("Paula Nieto", 6, dict(k8s="M", azure="M", terraform="M", english="X", cicd="P", monitoring="M"), "APPLIED", 6),
    ("Santiago Rojas", 4, dict(k8s="N", azure="P", terraform="P", english="M", cicd="M", monitoring="P"), "WITHDRAWN", 5),
    ("Valentina Cruz", 7, dict(k8s="M", azure="M", terraform="M", english="M", cicd="P", monitoring="P"), "APPLIED", 3),
    ("Tomás Herrera", 5, dict(k8s="M", azure="P", terraform="M", english="M", cicd="M", monitoring="M"), "APPLIED", 2),
]
CLOUD_UNREAD = [  # applications whose CV is not evaluated yet: (name, status, error, days ago, duplicate of)
    ("Isabela Duarte", "PROCESSING", None, 0.2, None),
    ("Mateo Salazar", "FAILED", "Escaneo sin texto legible", 1.5, None),
    ("Lucía Cárdenas", "PROCESSED", None, 1.0, "Daniela Osorio"),
]

DATA_PEOPLE = [
    ("Mariana Nieto", 4, dict(sql="M", powerbi="M", analyst_years="M", english_data="P", finance="N", dbt="M"), "SHORTLISTED", 17),
    ("Santiago Salazar", 3, dict(sql="M", powerbi="M", analyst_years="M", english_data="M", finance="M", dbt="P"), "INTERVIEW", 16),
    ("Sara Ortiz", 5, dict(sql="M", powerbi="M", analyst_years="M", english_data="N", finance="M", dbt="N"), "APPLIED", 14),
    ("Alejandro Restrepo", 2, dict(sql="P", powerbi="M", analyst_years="P", english_data="M", finance="M", dbt="N"), "APPLIED", 10),
    ("Natalia Franco", 6, dict(sql="M", powerbi="P", analyst_years="M", english_data="M", finance="M", dbt="N"), "HIRED", 19),
    ("Esteban Mora", 1, dict(sql="P", powerbi="P", analyst_years="X", english_data="P", finance="N", dbt="N"), "REJECTED", 8),
    ("Gabriela Zapata", 3, dict(sql="M", powerbi="M", analyst_years="M", english_data="P", finance="N", dbt="N"), "APPLIED", 4),
]

BACKEND_PEOPLE = [
    ("Sebastián Vega", 7, dict(dotnet="M", rest="M", git="M", sql_jr="M"), "APPLIED", 5),
    ("Martín Quiroga", 1, dict(dotnet="M", rest="P", git="M", sql_jr="P"), "SHORTLISTED", 7),
    ("Ximena Patiño", 1, dict(dotnet="P", rest="M", git="M", sql_jr="N"), "APPLIED", 6),
    ("Óscar Lozano", 2, dict(dotnet="M", rest="M", git="P", sql_jr="M"), "APPLIED", 3),
    ("Renata Gómez", 0, dict(dotnet="X", rest="P", git="M", sql_jr="P"), "APPLIED", 1),
]

OTHER_PEOPLE = [  # applications to other companies (no evaluation): name, vacancy, status, days ago
    ("Sebastián Vega", "devops", "APPLIED", 4),
]

TITLES = {
    "cloud": ["Ingeniero de plataforma", "Ingeniero DevOps", "Administrador de infraestructura"],
    "data": ["Analista de datos", "Analista de inteligencia de negocios", "Analista de riesgo"],
    "backend": ["Desarrollador backend", "Practicante de desarrollo", "Desarrollador web"],
}
EMPLOYERS = ["Sector financiero", "Empresa de software", "Retail", "Telecomunicaciones", "Consultoría", "Salud"]

SKILLS = {
    "k8s": ["Kubernetes", "AKS", "Helm"], "azure": ["Azure", "AWS"], "terraform": ["Terraform"],
    "english": [], "cicd": ["Azure DevOps", "GitHub Actions"], "monitoring": ["Prometheus", "Grafana"],
    "sql": ["SQL", "BigQuery"], "powerbi": ["Power BI", "DAX"], "analyst_years": ["Python", "Excel"],
    "english_data": [], "finance": ["Riesgo de crédito"], "dbt": ["dbt"],
    "dotnet": ["C#", ".NET", "Entity Framework"], "rest": ["APIs REST", "OpenAPI"], "git": ["Git", "GitHub"],
    "sql_jr": ["SQL Server"],
}
SCORE = {"M": (82, 98), "P": (45, 68), "X": (5, 25), "N": (0, 0)}
RESULT = {"M": "MEETS", "P": "PARTIALLY_MEETS", "X": "DOES_NOT_MEET", "N": "NO_EVIDENCE"}

out: list[str] = []
emit = out.append


def email_of(name: str) -> str:
    import unicodedata
    plain = unicodedata.normalize("NFD", name.lower())
    plain = "".join(ch for ch in plain if unicodedata.category(ch) != "Mn")
    return plain.replace(" ", ".") + "@correo-demo.co"


emit("-- Generated by scripts/seed/generate_demo.py. Fictional demo data; do not edit by hand.")
emit("-- Re-running it replaces the demo rows (fixed ids) and keeps everything else.")
emit("begin;")
emit("set constraints all deferred;")

company_ids = [uid(f"company:{key}") for key, *_ in COMPANIES]
ids_sql = ", ".join(q(i) for i in company_ids)
emit(f"delete from app.candidates where company_id in ({ids_sql});")
emit(f"delete from app.job_openings where company_id in ({ids_sql});")
emit(f"delete from app.interviewers where company_id in ({ids_sql});")
emit(f"delete from app.companies where id in ({ids_sql});")

for key, name, sector, size in COMPANIES:
    emit(f"insert into app.companies (id, name, sector, size, created_at) values ({q(uid('company:' + key))}, {q(name)}, {q(sector)}, {q(size)}, {ago(90)});")

vacancy_info: dict[str, dict] = {}
for (vkey, ckey, title, area, status, closes, positions, location, modality, contract, schedule, salary,
     published, description, reqs) in VACANCIES:
    vid = uid(f"vacancy:{vkey}")
    pid = uid(f"profile:{vkey}")
    created = (published or 3) + 2
    emit(
        "insert into app.job_openings (id, company_id, title, area, status, closing_at, positions, location, modality,"
        " contract_type, schedule, salary, published_at, created_at, updated_at) values ("
        f"{q(vid)}, {q(uid('company:' + ckey))}, {q(title)}, {q(area)}, {q(status)}, {ahead(closes) if closes >= 0 else ago(-closes)}, {positions},"
        f" {q(location)}, {q(modality)}, {q(contract)}, {q(schedule)}, {q(salary)}, {ago(published) if published else 'null'},"
        f" {ago(created)}, {ago(published or created)});"
    )
    emit(f"insert into app.role_profiles (id, job_opening_id, version, description, created_at) values ({q(pid)}, {q(vid)}, 1, {q(description)}, {ago(created)});")
    for rkey in reqs:
        rtype, weight, category, desc, equivalences, *_ = R[rkey]
        emit(
            "insert into app.requirements (id, role_profile_id, type, category, description, weight, equivalences) values ("
            f"{q(uid(f'req:{vkey}:{rkey}'))}, {q(pid)}, {q(rtype)}, {q(category)}, {q(desc)}, {weight}, {jq(equivalences)});"
        )
    vacancy_info[vkey] = {"id": vid, "profile": pid, "reqs": reqs, "company": ckey}

candidate_ids: dict[tuple[str, str], str] = {}


def candidate(name: str, company: str, days: float) -> str:
    cid_key = (company, name)
    if cid_key not in candidate_ids:
        cid = uid(f"candidate:{company}:{name}")
        candidate_ids[cid_key] = cid
        emit(f"insert into app.candidates (id, company_id, name, contact_email, created_at) values ({q(cid)}, {q(uid('company:' + company))}, {q(name)}, {q(email_of(name))}, {ago(days)});")
    return candidate_ids[cid_key]


def professional_data(vkey: str, years: int, profile: dict[str, str]) -> dict:
    titles = TITLES[vkey]
    skills: list[str] = []
    for rkey, mark in profile.items():
        if mark in "MP":
            skills.extend(SKILLS.get(rkey, []))
    first_years = max(1, years - 2)
    jobs = [
        {
            "title": titles[0],
            "company": random.choice(EMPLOYERS),
            "start_date": f"{2026 - first_years}",
            "end_date": "Current",
            "months": first_years * 12,
            "highlights": [],
            "technologies": skills[:4],
        }
    ]
    if years > 2:
        jobs.append({
            "title": titles[1],
            "company": random.choice(EMPLOYERS),
            "start_date": f"{2026 - years}",
            "end_date": f"{2026 - first_years}",
            "months": (years - first_years) * 12,
            "highlights": [],
            "technologies": skills[4:7],
        })
    for rkey, mark in profile.items():
        meets, partial = R[rkey][5], R[rkey][6]
        text = meets if mark == "M" else partial if mark == "P" else None
        if text:
            jobs[0 if len(jobs[0]["highlights"]) < 3 or len(jobs) == 1 else 1]["highlights"].append(text.format(y=max(1, years - 1)))
    english = profile.get("english") or profile.get("english_data")
    languages = ["Español nativo"] + (["Inglés B2"] if english == "M" else ["Inglés intermedio"] if english == "P" else [])
    return {
        "summary": f"{titles[0]} con {years} años de experiencia." if years else f"Recién graduado, interesado en {titles[0].lower()}.",
        "skills": skills,
        "work_experience": jobs,
        "education": [{"degree": "Ingeniería de Sistemas" if vkey != "data" else "Ingeniería Industrial", "field": None, "end_date": str(2026 - years - 1)}],
        "certifications": ["AWS Solutions Architect – Associate"] if vkey == "cloud" and years >= 6 else [],
        "languages": languages,
        "seniority_level": "senior" if years >= 6 else "mid" if years >= 3 else "junior",
        "total_experience_months": years * 12,
        "extractor_version": "v1.0.0",
        "parser_strategy": "native_layout",
    }


resume_versions: dict[str, int] = {}


def resume(cid: str, key: str, name: str, days: float, status: str, data: dict | None, error: str | None, file_hash: str | None):
    rid = uid(f"resume:{key}")
    version = resume_versions[cid] = resume_versions.get(cid, 0) + 1
    file_name = email_of(name).split("@")[0].replace(".", "_") + "_cv.pdf"
    emit(
        "insert into app.resumes (id, candidate_id, version, file_key, file_hash, processing_status, processing_error, professional_data,"
        " extractor_version, file_name, content_type, size_bytes, created_at) values ("
        f"{q(rid)}, {q(cid)}, {version}, {q('demo/' + rid + '.pdf')}, {q(file_hash or hashlib.sha256(rid.encode()).hexdigest())}, {q(status)}, {q(error)},"
        f" {jq(data) if data else 'null'}, {q('v1.0.0' if data else None)}, {q(file_name)}, 'application/pdf', {random.randint(90, 260) * 1024}, {ago(days)});"
    )
    fragments: list[tuple[str, str]] = []
    if data:
        position = 0
        for job in data["work_experience"]:
            for line in job["highlights"]:
                fid = uid(f"fragment:{key}:{position}")
                page = 1 if position < 3 else 2
                emit(
                    "insert into app.resume_fragments (id, resume_id, position, section, source_location, anonymized_content) values ("
                    f"{q(fid)}, {q(rid)}, {position}, 'experience', {jq({'page_number': page, 'char_start': position * 120, 'char_end': position * 120 + len(line)})}, {q(line)});"
                )
                fragments.append((fid, line))
                position += 1
        for lang in data["languages"]:
            fid = uid(f"fragment:{key}:{position}")
            emit(
                "insert into app.resume_fragments (id, resume_id, position, section, source_location, anonymized_content) values ("
                f"{q(fid)}, {q(rid)}, {position}, 'languages', {jq({'page_number': 2, 'char_start': 900, 'char_end': 900 + len(lang)})}, {q(lang)});"
            )
            fragments.append((fid, lang))
            position += 1
    return rid, fragments


def evaluate(vkey: str, app_id: str, rid: str, fragments, profile: dict[str, str], days: float) -> tuple[str, float]:
    info = vacancy_info[vkey]
    eid = uid(f"evaluation:{app_id}")
    rows = []
    total = 0.0
    mandatory_ok = True
    mandatory_hard_fail = False
    for rkey in info["reqs"]:
        rtype, weight, _, desc, _, meets, partial = R[rkey]
        if rtype == "DISQUALIFYING":
            rows.append((rkey, "DOES_NOT_MEET", 0, 0.95, "No se encontró nada que impida contratar a la persona."))
            continue
        mark = profile.get(rkey, "N")
        lo, hi = SCORE[mark]
        score = random.randint(lo, hi) if hi else 0
        total += weight * score / 100
        if rtype == "MANDATORY" and mark != "M":
            mandatory_ok = False
            if mark in "XN":
                mandatory_hard_fail = True
        justification = {
            "M": f"La hoja de vida muestra {desc.lower()} con evidencia concreta.",
            "P": f"Hay señales de {desc.lower()}, pero no alcanzan lo pedido.",
            "X": f"Lo que muestra la hoja de vida no cumple {desc.lower()}.",
            "N": f"La hoja de vida no menciona {desc.lower()}.",
        }[mark]
        rows.append((rkey, RESULT[mark], score, round(random.uniform(0.72, 0.95), 2), justification))
    fulfillment = "MEETS" if mandatory_ok else "DOES_NOT_MEET" if mandatory_hard_fail else "PARTIALLY_MEETS"
    total = round(total, 1)
    strengths = [desc for (rkey, res, *_ ) in rows for desc in [R[rkey][3]] if res == "MEETS" and R[rkey][0] != "DISQUALIFYING"][:3]
    gaps = [R[rkey][3] for (rkey, res, *_ ) in rows if res in ("PARTIALLY_MEETS", "NO_EVIDENCE", "DOES_NOT_MEET") and R[rkey][0] != "DISQUALIFYING"][:3]
    emit(
        "insert into app.evaluations (id, application_id, role_profile_id, resume_id, processing_status, total_score, fulfillment, disqualified,"
        " confidence, strengths, gaps, recommend_interview, ai_model, prompt_version, created_at) values ("
        f"{q(eid)}, {q(app_id)}, {q(info['profile'])}, {q(rid)}, 'COMPLETED', {total}, {q(fulfillment)}, false,"
        f" {round(random.uniform(0.74, 0.93), 2)}, {jq(strengths)}, {jq(gaps)}, {q(fulfillment != 'DOES_NOT_MEET')}, 'claude-sonnet-5-5', 'evaluate-v1', {ago(days)});"
    )
    for rkey, result, score, confidence, justification in rows:
        rrid = uid(f"result:{eid}:{rkey}")
        emit(
            "insert into app.requirement_results (id, evaluation_id, requirement_id, result, score, confidence, justification) values ("
            f"{q(rrid)}, {q(eid)}, {q(uid(f'req:{vkey}:{rkey}'))}, {q(result)}, {score}, {confidence}, {q(justification)});"
        )
        if result in ("MEETS", "PARTIALLY_MEETS"):
            phrase = (R[rkey][5] if result == "MEETS" else R[rkey][6]) or ""
            stem = phrase.split("{")[0][:25].lower()
            match = next((f for f in fragments if stem and stem in f[1].lower()), None)
            if match:
                emit(f"insert into app.evidence (id, requirement_result_id, resume_fragment_id, citation) values ({q(uid(f'evidence:{rrid}'))}, {q(rrid)}, {q(match[0])}, {q(match[1])});")
    return eid, total


def application(vkey: str, cid: str, name: str, status: str, days: float) -> str:
    aid = uid(f"application:{vkey}:{name}")
    updated = max(0.1, days - random.uniform(1, 4)) if status != "APPLIED" else days
    emit(
        "insert into app.applications (id, job_opening_id, candidate_id, source, selection_status, created_at, updated_at) values ("
        f"{q(aid)}, {q(vacancy_info[vkey]['id'])}, {q(cid)}, 'CANDIDATE', {q(status)}, {ago(days)}, {ago(updated)});"
    )
    return aid


interviews: list[tuple] = []

for vkey, people in (("cloud", CLOUD_PEOPLE), ("data", DATA_PEOPLE), ("backend", BACKEND_PEOPLE)):
    company = vacancy_info[vkey]["company"]
    for index, (name, years, profile, status, days) in enumerate(people):
        cid = candidate(name, company, days)
        data = professional_data(vkey, years, profile)
        rid, fragments = resume(cid, f"{vkey}:{name}", name, days, "PROCESSED", data, None, None)
        aid = application(vkey, cid, name, status, days)
        eid, total = evaluate(vkey, aid, rid, fragments, profile, max(0.05, days - 0.3))
        if status == "INTERVIEW":
            interviews.append((vkey, name, aid, eid, days))

for name, status, error, days, duplicate_of in CLOUD_UNREAD:
    cid = candidate(name, "cordillera", days)
    dup_hash = hashlib.sha256(uid(f"resume:cloud:{duplicate_of}").encode()).hexdigest() if duplicate_of else None
    data = professional_data("cloud", 4, dict(k8s="M", azure="P", terraform="N", english="M")) if status == "PROCESSED" else None
    resume(cid, f"cloud:{name}", name, days, status, data, error, dup_hash)
    application("cloud", cid, name, "APPLIED", days)

for name, vkey, status, days in OTHER_PEOPLE:
    cid = candidate(name, vacancy_info[vkey]["company"], days)
    data = professional_data("cloud", 7, dict(k8s="M", azure="M", terraform="M", english="P", cicd="M", monitoring="M"))
    resume(cid, f"{vkey}:{name}", name, days, "PROCESSED", data, None, None)
    application(vkey, cid, name, status, days)

# Interviews: the first one finished (with a contrast), the others waiting with a link token.
for index, (vkey, name, aid, eid, days) in enumerate(interviews):
    iid = uid(f"interview:{aid}")
    done = index == 0 or vkey == "data"
    token = hashlib.sha256(f"criba-demo-token:{aid}".encode()).hexdigest()[:32]
    started = max(0.2, days - 5)
    reqs = [k for k in vacancy_info[vkey]["reqs"] if R[k][0] != "DISQUALIFYING"][:5]
    contrasts = (
        {
            "cloud": [
                {"label": "Servicios migrados", "cvSays": "30 servicios a Azure", "interviewSays": "Dijo 32, con 4 en curso", "verdict": "match"},
                {"label": "Inglés", "cvSays": "B2", "interviewSays": "Respondió en inglés con fluidez", "verdict": "match"},
            ],
            "data": [
                {"label": "Tableros", "cvSays": "Doce tableros en Power BI", "interviewSays": "Habló de diez, dos en construcción", "verdict": "match"},
                {"label": "dbt", "cvSays": "Lo conoce por un curso", "interviewSays": "Dijo usarlo a diario", "verdict": "conflict"},
            ],
        }.get(vkey)
        if done else None
    )
    emit(
        "insert into app.interviews (id, evaluation_id, status, ai_model, prompt_version, inconsistencies, recommendation, created_at, started_at, completed_at, token, expires_at, language) values ("
        f"{q(iid)}, {q(eid)}, {q('COMPLETED' if done else 'SCHEDULED')}, 'claude-sonnet-5-5', 'questions-v1', {jq(contrasts) if contrasts else 'null'},"
        f" {q('ADVANCE' if done else None)}, {ago(started + 1)}, {ago(started) if done else 'null'}, {ago(started - 0.02) if done else 'null'},"
        f" {q(token)}, {ahead(3) if not done else ago(started - 3)}, 'es-en');"
    )
    for order, rkey in enumerate(reqs, start=1):
        desc = R[rkey][3]
        question = f"Sobre «{desc}»: cuéntame el proyecto más exigente en el que lo aplicaste y cuál fue tu papel."
        closing = ["Yo lideré la parte técnica.", "Lo documenté para el equipo.", "Medimos el resultado cada mes.", "Lo hice con otra persona del equipo.", "Fue mi primer proyecto en ese cargo."][order % 5]
        answer = f"{(R[rkey][5] or R[rkey][6] or 'Lo usé en mi trabajo actual.').format(y=4)} {closing}" if done else None
        emit(
            "insert into app.interview_turns (interview_id, turn_order, topic, question, answer, answer_evaluation) values ("
            f"{q(iid)}, {order}, {q(R[rkey][2])}, {q(question)}, {q(answer)}, {q('match' if done else None)});"
        )

emit("commit;")
sys.stdout.reconfigure(encoding="utf-8", newline="\n")
print("\n".join(out))
