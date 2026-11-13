using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class ApplicationRepository(AppDbContext db) : IApplicationRepository
{
    public Task<ApplicationEntity?> FindByJobAndCandidateAsync(Guid jobOpeningId, Guid candidateId, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(
            application => application.JobOpeningId == jobOpeningId && application.CandidateId == candidateId,
            ct);

    public Task<ApplicationEntity?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Applications.FirstOrDefaultAsync(application => application.Id == id, ct);

    public async Task<IReadOnlyList<ApplicationSubmission>> ListByJobOpeningAsync(Guid jobOpeningId, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQuery<SubmissionRow>($"""
            select
              a.id                 as "ApplicationId",
              c.id                 as "CandidateId",
              c.name               as "CandidateName",
              c.contact_email      as "CandidateEmail",
              a.source             as "Source",
              a.selection_status   as "SelectionStatus",
              a.created_at         as "AppliedAt",
              a.updated_at         as "UpdatedAt",
              r.id                 as "ResumeId",
              r.version            as "ResumeVersion",
              r.processing_status  as "ProcessingStatus",
              r.processing_error   as "ProcessingError",
              r.created_at         as "ResumeCreatedAt",
              r.file_name          as "FileName",
              r.content_type       as "ContentType",
              r.size_bytes         as "SizeBytes",
              (select max((f.source_location ->> 'page_number')::int)
                 from app.resume_fragments f where f.resume_id = r.id) as "Pages",
              case when jsonb_typeof(r.professional_data -> 'work_experience') = 'array'
                   then jsonb_array_length(r.professional_data -> 'work_experience') end as "JobsRead",
              case when jsonb_typeof(r.professional_data -> 'certifications') = 'array'
                   then jsonb_array_length(r.professional_data -> 'certifications') end as "CertificationsRead",
              (select coalesce(r2.file_name, c2.name)
                 from app.applications a2
                 join app.candidates c2 on c2.id = a2.candidate_id
                 join app.resumes r2 on r2.candidate_id = c2.id
                where a2.job_opening_id = a.job_opening_id
                  and a2.id <> a.id
                  and a2.created_at < a.created_at
                  and r.file_hash is not null
                  and r2.file_hash = r.file_hash
                order by a2.created_at
                limit 1)           as "DuplicateOf",
              e.fulfillment        as "Fulfillment",
              e.total_score        as "Score"
            from app.applications a
            join app.candidates c on c.id = a.candidate_id
            left join lateral (
              select rs.*
              from app.resumes rs
              where rs.candidate_id = a.candidate_id
              order by rs.version desc, rs.created_at desc
              limit 1
            ) r on true
            left join lateral (
              select ev.fulfillment, ev.total_score
              from app.evaluations ev
              where ev.application_id = a.id and ev.processing_status = 'COMPLETED'
              order by ev.created_at desc
              limit 1
            ) e on true
            where a.job_opening_id = {jobOpeningId}
            order by a.created_at desc
            """).ToListAsync(ct);

        return rows.Select(row => new ApplicationSubmission(
            row.ApplicationId,
            row.CandidateId,
            row.CandidateName,
            row.CandidateEmail,
            row.Source,
            row.SelectionStatus,
            row.AppliedAt,
            row.UpdatedAt,
            row.ResumeId,
            row.ResumeVersion,
            row.ProcessingStatus,
            row.ProcessingError,
            row.ResumeCreatedAt,
            row.FileName,
            row.ContentType,
            row.SizeBytes,
            row.Pages,
            row.JobsRead,
            row.CertificationsRead,
            row.DuplicateOf,
            row.Fulfillment,
            row.Score)).ToList();
    }

    internal sealed class SubmissionRow
    {
        public Guid ApplicationId { get; set; }

        public Guid CandidateId { get; set; }

        public string CandidateName { get; set; } = string.Empty;

        public string CandidateEmail { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string SelectionStatus { get; set; } = string.Empty;

        public DateTimeOffset AppliedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public Guid? ResumeId { get; set; }

        public int? ResumeVersion { get; set; }

        public string? ProcessingStatus { get; set; }

        public string? ProcessingError { get; set; }

        public DateTimeOffset? ResumeCreatedAt { get; set; }

        public string? FileName { get; set; }

        public string? ContentType { get; set; }

        public long? SizeBytes { get; set; }

        public int? Pages { get; set; }

        public int? JobsRead { get; set; }

        public int? CertificationsRead { get; set; }

        public string? DuplicateOf { get; set; }

        public string? Fulfillment { get; set; }

        public decimal? Score { get; set; }
    }

    public void Add(ApplicationEntity application) => db.Applications.Add(application);

    public void Remove(ApplicationEntity application) => db.Applications.Remove(application);
}
