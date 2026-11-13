using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL read/write model for what the candidate sees: the public board, the applicant portal (found by the
/// e-mail of the magic-link session) and the AI interview link. Interviews and evaluations are not mapped in EF.
/// </summary>
internal sealed class CandidateSideStore(AppDbContext db) : ICandidateSideStore
{
    public async Task<IReadOnlyList<PublicJobRow>> ListPublicJobsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<PublicJobSql>($"""
            select jo.id as "Id", jo.title as "Title", co.name as "Company", jo.area as "Area",
                   jo.location as "Location", jo.modality as "Modality",
                   jo.closing_at as "ClosingAt", coalesce(jo.published_at, jo.updated_at) as "PublishedAt"
            from app.job_openings jo
            join app.companies co on co.id = jo.company_id
            where jo.status = 'OPEN'
              and jo.deleted_at is null
              and (jo.closing_at is null or jo.closing_at > {now})
            order by coalesce(jo.published_at, jo.updated_at) desc
            limit 200
            """).ToListAsync(ct);

        return rows.Select(row => new PublicJobRow(row.Id, row.Title, row.Company, row.Area, row.Location, row.Modality, row.ClosingAt, row.PublishedAt)).ToList();
    }

    public async Task<IReadOnlyList<PublicJobRequirementRow>> ListPublicRequirementsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default)
    {
        if (jobIds.Count == 0)
        {
            return [];
        }

        var ids = jobIds.ToArray();
        var rows = await db.Database.SqlQuery<RequirementSql>($"""
            select rp.job_opening_id as "JobId", r.type as "Type", r.description as "Description",
                   array(select jsonb_object_keys(r.equivalences)) as "Equivalences"
            from app.requirements r
            join app.role_profiles rp on rp.id = r.role_profile_id
            where rp.job_opening_id = any({ids})
              and rp.version = (select max(version) from app.role_profiles where job_opening_id = rp.job_opening_id)
            order by rp.job_opening_id, r.weight desc, r.description
            """).ToListAsync(ct);

        return rows.Select(row => new PublicJobRequirementRow(row.JobId, row.Type, row.Description, row.Equivalences ?? [])).ToList();
    }

    public async Task<IReadOnlyList<ApplicantApplicationRow>> ListApplicantApplicationsAsync(string email, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<ApplicantApplicationSql>($"""
            select a.id as "ApplicationId", jo.id as "JobId", jo.title as "JobTitle", co.name as "Company",
                   jo.area as "JobArea", jo.location as "Location", jo.modality as "Modality",
                   c.name as "CandidateName", a.selection_status as "SelectionStatus",
                   a.created_at as "AppliedAt", a.updated_at as "UpdatedAt",
                   (select min(e.created_at) from app.evaluations e
                     where e.application_id = a.id and e.processing_status = 'COMPLETED') as "EvaluatedAt",
                   i.status as "InterviewStatus", i.token as "InterviewToken",
                   i.created_at as "InterviewCreatedAt", i.expires_at as "InterviewExpiresAt"
            from app.candidates c
            join app.applications a on a.candidate_id = c.id
            join app.job_openings jo on jo.id = a.job_opening_id
            join app.companies co on co.id = jo.company_id
            left join lateral (
              select iv.status, iv.token, iv.created_at, iv.expires_at
              from app.interviews iv
              join app.evaluations ev on ev.id = iv.evaluation_id
              where ev.application_id = a.id and iv.status <> 'CANCELLED'
              order by iv.created_at desc
              limit 1
            ) i on true
            -- The session e-mail, plus what this user sent signed in with another e-mail.
            where (lower(c.contact_email) = lower({email}) or a.submitted_by in (select u.id from auth.users u where lower(u.email) = lower({email})))
              and jo.deleted_at is null
            order by a.updated_at desc
            """).ToListAsync(ct);

        return rows.Select(row => new ApplicantApplicationRow(
            row.ApplicationId, row.JobId, row.JobTitle, row.Company, row.JobArea, row.Location, row.Modality,
            row.CandidateName, row.SelectionStatus, row.AppliedAt, row.UpdatedAt, row.EvaluatedAt,
            row.InterviewStatus, row.InterviewToken, row.InterviewCreatedAt, row.InterviewExpiresAt)).ToList();
    }

    public async Task<ApplicantResumeRow?> FindLatestResumeAsync(string email, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<ResumeSql>($"""
            select r.id as "ResumeId", c.name as "CandidateName", r.file_key as "FileKey", r.file_name as "FileName",
                   r.content_type as "ContentType", r.size_bytes as "SizeBytes", r.created_at as "CreatedAt",
                   r.professional_data::text as "ProfessionalData", r.processing_status as "ProcessingStatus"
            from app.resumes r
            join app.candidates c on c.id = r.candidate_id
            where lower(c.contact_email) = lower({email})
               or exists (select 1 from app.applications sa where sa.candidate_id = c.id and sa.submitted_by in (select u.id from auth.users u where lower(u.email) = lower({email})))
            order by r.created_at desc
            limit 1
            """).ToListAsync(ct);

        return rows.Select(row => new ApplicantResumeRow(row.ResumeId, row.CandidateName, row.FileKey, row.FileName, row.ContentType, row.SizeBytes, row.CreatedAt, row.ProfessionalData, row.ProcessingStatus)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<ResumeVersionRow>> ListResumeVersionsAsync(string email, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<ResumeVersionSql>($"""
            select coalesce(min(r.file_name), 'hoja_de_vida.pdf') as "FileName", min(r.created_at) as "CreatedAt"
            from app.resumes r
            join app.candidates c on c.id = r.candidate_id
            where lower(c.contact_email) = lower({email})
            group by coalesce(r.file_hash, r.id::text)
            order by min(r.created_at) desc
            limit 20
            """).ToListAsync(ct);
        return rows.Select(row => new ResumeVersionRow(row.FileName, row.CreatedAt)).ToList();
    }

    public async Task<bool> WithdrawAsync(string email, Guid applicationId, CancellationToken ct = default)
    {
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update app.applications a
            set selection_status = 'WITHDRAWN'
            from app.candidates c
            where c.id = a.candidate_id
              and a.id = {applicationId}
              and (lower(c.contact_email) = lower({email}) or a.submitted_by in (select u.id from auth.users u where lower(u.email) = lower({email})))
              and a.selection_status in ('APPLIED', 'SHORTLISTED', 'INTERVIEW')
            """, ct);

        if (changed > 0)
        {
            // A withdrawn person can no longer answer a pending interview.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                update app.interviews i
                set status = 'CANCELLED'
                from app.evaluations e
                where e.id = i.evaluation_id
                  and e.application_id = {applicationId}
                  and i.status in ('SCHEDULED', 'IN_PROGRESS')
                """, ct);
        }

        return changed > 0;
    }

    public Task<int> EraseAsync(string email, CancellationToken ct = default) =>
        db.Database.ExecuteSqlInterpolatedAsync($"delete from app.candidates where lower(contact_email) = lower({email})", ct);

    public async Task<InterviewTarget?> FindInterviewTargetAsync(Guid companyId, Guid vacancyId, Guid applicationId, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<InterviewTargetSql>($"""
            select a.id as "ApplicationId", jo.id as "VacancyId", jo.title as "VacancyTitle", co.name as "CompanyName",
                   a.selection_status as "SelectionStatus", c.name as "CandidateName", c.contact_email as "CandidateEmail",
                   (select e.id from app.evaluations e
                     where e.application_id = a.id and e.processing_status = 'COMPLETED'
                     order by e.created_at desc limit 1) as "EvaluationId"
            from app.applications a
            join app.job_openings jo on jo.id = a.job_opening_id
            join app.companies co on co.id = jo.company_id
            join app.candidates c on c.id = a.candidate_id
            where a.id = {applicationId}
              and jo.id = {vacancyId}
              and jo.company_id = {companyId}
              and jo.deleted_at is null
            """).ToListAsync(ct);

        return rows.Select(row => new InterviewTarget(row.ApplicationId, row.VacancyId, row.VacancyTitle, row.CompanyName, row.SelectionStatus, row.CandidateName, row.CandidateEmail, row.EvaluationId)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<RequirementGapRow>> ListRequirementGapsAsync(Guid evaluationId, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<RequirementGapSql>($"""
            select r.description as "Description", r.category as "Category", r.type as "Type", rr.result as "Result"
            from app.evaluations e
            join app.requirements r on r.role_profile_id = e.role_profile_id
            left join app.requirement_results rr on rr.evaluation_id = e.id and rr.requirement_id = r.id
            where e.id = {evaluationId}
              and r.type <> 'DISQUALIFYING'
            order by r.weight desc, r.description
            """).ToListAsync(ct);

        return rows.Select(row => new RequirementGapRow(row.Description, row.Category, row.Type, row.Result)).ToList();
    }

    public async Task<Guid> CreateInterviewAsync(
        Guid evaluationId,
        Guid applicationId,
        Guid interviewerId,
        string token,
        DateTimeOffset expiresAt,
        string language,
        IReadOnlyList<InterviewQuestionSeed> questions,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            update app.interviews i
            set status = 'CANCELLED'
            from app.evaluations e
            where e.id = i.evaluation_id
              and e.application_id = {applicationId}
              and i.status in ('SCHEDULED', 'IN_PROGRESS')
            """, ct);

        var interviewId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into app.interviews (id, evaluation_id, interviewer_id, status, token, expires_at, language, prompt_version)
            values ({interviewId}, {evaluationId}, {interviewerId}, 'SCHEDULED', {token}, {expiresAt}, {language}, 'questions-v1')
            """, ct);

        for (var index = 0; index < questions.Count; index++)
        {
            var order = index + 1;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                insert into app.interview_turns (interview_id, turn_order, topic, question)
                values ({interviewId}, {order}, {questions[index].Topic}, {questions[index].Question})
                """, ct);
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            update app.applications
            set selection_status = 'INTERVIEW'
            where id = {applicationId} and selection_status in ('APPLIED', 'SHORTLISTED')
            """, ct);

        await transaction.CommitAsync(ct);
        return interviewId;
    }

    public async Task<PublicInterviewRow?> FindInterviewByTokenAsync(string token, CancellationToken ct = default)
    {
        var headers = await db.Database.SqlQuery<PublicInterviewSql>($"""
            select i.id as "InterviewId", i.status as "Status", i.expires_at as "ExpiresAt",
                   c.name as "CandidateName", jo.title as "JobTitle", co.name as "Company"
            from app.interviews i
            join app.evaluations e on e.id = i.evaluation_id
            join app.applications a on a.id = e.application_id
            join app.candidates c on c.id = a.candidate_id
            join app.job_openings jo on jo.id = a.job_opening_id
            join app.companies co on co.id = jo.company_id
            where i.token = {token}
            """).ToListAsync(ct);

        var header = headers.FirstOrDefault();
        if (header is null)
        {
            return null;
        }

        var turns = await db.Database.SqlQuery<PublicTurnSql>($"""
            select turn_order as "Order", question as "Question", answer as "Answer"
            from app.interview_turns
            where interview_id = {header.InterviewId}
            order by turn_order
            """).ToListAsync(ct);

        return new PublicInterviewRow(
            header.InterviewId,
            header.Status,
            header.ExpiresAt,
            header.CandidateName,
            header.JobTitle,
            header.Company,
            turns.Select(turn => new PublicTurnRow(turn.Order, turn.Question, turn.Answer)).ToList());
    }

    public async Task<bool> SaveAnswerAsync(Guid interviewId, int order, string answer, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // A follow-up answer to the same question is appended, never lost.
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update app.interview_turns
            set answer = case when answer is null or answer = '' then {answer} else answer || E'\n\n' || {answer} end
            where interview_id = {interviewId} and turn_order = {order}
            """, ct);
        if (changed == 0)
        {
            return false;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            update app.interviews
            set status = 'IN_PROGRESS', started_at = coalesce(started_at, now())
            where id = {interviewId} and status = 'SCHEDULED'
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            update app.interviews i
            set status = 'COMPLETED', completed_at = now(), recommendation = coalesce(i.recommendation, 'REVIEW')
            where i.id = {interviewId}
              and i.status = 'IN_PROGRESS'
              and not exists (
                select 1 from app.interview_turns t
                where t.interview_id = i.id and (t.answer is null or t.answer = ''))
            """, ct);

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<InterviewContactRow?> FindInterviewContactAsync(Guid companyId, Guid interviewId, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<InterviewContactSql>($"""
            select i.id as "InterviewId", a.id as "ApplicationId", c.name as "CandidateName", c.contact_email as "CandidateEmail",
                   jo.title as "JobTitle", co.name as "CompanyName"
            from app.interviews i
            join app.evaluations e on e.id = i.evaluation_id
            join app.applications a on a.id = e.application_id
            join app.candidates c on c.id = a.candidate_id
            join app.job_openings jo on jo.id = a.job_opening_id
            join app.companies co on co.id = jo.company_id
            where i.id = {interviewId} and jo.company_id = {companyId} and jo.deleted_at is null
            """).ToListAsync(ct);

        return rows.Select(row => new InterviewContactRow(row.InterviewId, row.ApplicationId, row.CandidateName, row.CandidateEmail, row.JobTitle, row.CompanyName)).FirstOrDefault();
    }

    public async Task<string?> FindResumeKeyAsync(Guid companyId, Guid vacancyId, Guid applicationId, CancellationToken ct = default)
    {
        var keys = await db.Database.SqlQuery<string>($"""
            select r.file_key as "Value"
            from app.applications a
            join app.job_openings jo on jo.id = a.job_opening_id
            join app.resumes r on r.candidate_id = a.candidate_id
            where a.id = {applicationId} and jo.id = {vacancyId} and jo.company_id = {companyId} and jo.deleted_at is null
            order by r.version desc, r.created_at desc
            limit 1
            """).ToListAsync(ct);
        return keys.FirstOrDefault();
    }

    public async Task<IReadOnlyList<AiActivityRow>> ListAiActivityAsync(Guid companyId, int take, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<AiActivitySql>($"""
            with company_apps as (
              select a.id, a.candidate_id, jo.title
              from app.applications a
              join app.job_openings jo on jo.id = a.job_opening_id
              where jo.company_id = {companyId} and jo.deleted_at is null
            ),
            ranked as (
              select e.id, e.application_id, e.created_at, e.fulfillment, e.disqualified,
                     row_number() over (partition by e.role_profile_id order by e.total_score desc nulls last) as position,
                     count(*) over (partition by e.role_profile_id) as total
              from app.evaluations e
              join company_apps ca on ca.id = e.application_id
              where e.processing_status = 'COMPLETED'
            )
            select * from (
              select case r.processing_status when 'FAILED' then 'unreadable' else 'read' end as "Kind",
                     r.created_at as "At", ca.title as "JobTitle", r.file_name as "FileName",
                     (select max((f.source_location ->> 'page_number')::int) from app.resume_fragments f where f.resume_id = r.id) as "Pages",
                     null::text as "Fulfillment", null::int as "Position", null::int as "Of"
              from app.resumes r
              join company_apps ca on ca.candidate_id = r.candidate_id
              where r.processing_status in ('PROCESSED', 'FAILED')
              union all
              select 'evaluated', rk.created_at, ca.title, null, null,
                     case when rk.disqualified then 'DOES_NOT_MEET' else rk.fulfillment end,
                     rk.position::int, rk.total::int
              from ranked rk
              join company_apps ca on ca.id = rk.application_id
              union all
              select 'interview', coalesce(i.completed_at, i.created_at), ca.title, null, null, null, null, null
              from app.interviews i
              join app.evaluations e on e.id = i.evaluation_id
              join company_apps ca on ca.id = e.application_id
              where i.status = 'COMPLETED'
            ) activity
            order by "At" desc
            limit {take}
            """).ToListAsync(ct);

        return rows.Select(row => new AiActivityRow(row.Kind, row.At, row.JobTitle, row.FileName, row.Pages, row.Fulfillment, row.Position, row.Of)).ToList();
    }

    internal sealed class PublicJobSql
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Company { get; set; } = string.Empty;

        public string? Area { get; set; }

        public string? Location { get; set; }

        public string? Modality { get; set; }

        public DateTimeOffset? ClosingAt { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }

    internal sealed class RequirementSql
    {
        public Guid JobId { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string[]? Equivalences { get; set; }
    }

    internal sealed class ApplicantApplicationSql
    {
        public Guid ApplicationId { get; set; }

        public Guid JobId { get; set; }

        public string JobTitle { get; set; } = string.Empty;

        public string Company { get; set; } = string.Empty;

        public string? JobArea { get; set; }

        public string? Location { get; set; }

        public string? Modality { get; set; }

        public string CandidateName { get; set; } = string.Empty;

        public string SelectionStatus { get; set; } = string.Empty;

        public DateTimeOffset AppliedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public DateTimeOffset? EvaluatedAt { get; set; }

        public string? InterviewStatus { get; set; }

        public string? InterviewToken { get; set; }

        public DateTimeOffset? InterviewCreatedAt { get; set; }

        public DateTimeOffset? InterviewExpiresAt { get; set; }
    }

    internal sealed class ResumeSql
    {
        public Guid ResumeId { get; set; }

        public string CandidateName { get; set; } = string.Empty;

        public string FileKey { get; set; } = string.Empty;

        public string? FileName { get; set; }

        public string? ContentType { get; set; }

        public long? SizeBytes { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public string? ProfessionalData { get; set; }

        public string ProcessingStatus { get; set; } = string.Empty;
    }

    internal sealed class ResumeVersionSql
    {
        public string FileName { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
    }

    internal sealed class InterviewTargetSql
    {
        public Guid ApplicationId { get; set; }

        public Guid VacancyId { get; set; }

        public string VacancyTitle { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string SelectionStatus { get; set; } = string.Empty;

        public string CandidateName { get; set; } = string.Empty;

        public string CandidateEmail { get; set; } = string.Empty;

        public Guid? EvaluationId { get; set; }
    }

    internal sealed class RequirementGapSql
    {
        public string Description { get; set; } = string.Empty;

        public string? Category { get; set; }

        public string Type { get; set; } = string.Empty;

        public string? Result { get; set; }
    }

    internal sealed class PublicInterviewSql
    {
        public Guid InterviewId { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTimeOffset? ExpiresAt { get; set; }

        public string CandidateName { get; set; } = string.Empty;

        public string JobTitle { get; set; } = string.Empty;

        public string Company { get; set; } = string.Empty;
    }

    internal sealed class PublicTurnSql
    {
        public int Order { get; set; }

        public string Question { get; set; } = string.Empty;

        public string? Answer { get; set; }
    }

    internal sealed class InterviewContactSql
    {
        public Guid InterviewId { get; set; }

        public Guid ApplicationId { get; set; }

        public string CandidateName { get; set; } = string.Empty;

        public string CandidateEmail { get; set; } = string.Empty;

        public string JobTitle { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;
    }

    internal sealed class AiActivitySql
    {
        public string Kind { get; set; } = string.Empty;

        public DateTimeOffset At { get; set; }

        public string JobTitle { get; set; } = string.Empty;

        public string? FileName { get; set; }

        public int? Pages { get; set; }

        public string? Fulfillment { get; set; }

        public int? Position { get; set; }

        public int? Of { get; set; }
    }
}
