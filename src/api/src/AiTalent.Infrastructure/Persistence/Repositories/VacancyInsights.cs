using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

/// <summary>
/// Evaluations are written by the AI Engine and are not mapped in EF, so this read model uses SQL.
/// Latest resume per candidate (same rule as the applications list) and latest COMPLETED evaluation per application.
/// </summary>
internal sealed class VacancyInsights(AppDbContext db) : IVacancyInsights
{
    public async Task<IReadOnlyList<ApplicationFact>> ListApplicationFactsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> vacancyIds,
        CancellationToken ct = default)
    {
        if (vacancyIds.Count == 0)
        {
            return [];
        }

        var ids = vacancyIds.Distinct().ToArray();
        var rows = await db.Database.SqlQuery<FactRow>($"""
            select
              a.id                as "ApplicationId",
              a.job_opening_id    as "JobOpeningId",
              c.name              as "CandidateName",
              a.selection_status  as "SelectionStatus",
              a.created_at        as "AppliedAt",
              r.processing_status as "ResumeStatus",
              r.file_hash         as "ResumeHash",
              r.created_at        as "ResumeCreatedAt",
              e.fulfillment       as "Fulfillment",
              coalesce(e.disqualified, false) as "Disqualified",
              e.total_score       as "TotalScore",
              e.created_at        as "EvaluatedAt"
            from app.applications a
            join app.job_openings j on j.id = a.job_opening_id
            join app.candidates c on c.id = a.candidate_id
            left join lateral (
              select rs.processing_status, rs.file_hash, rs.created_at
              from app.resumes rs
              where rs.candidate_id = a.candidate_id
              order by rs.version desc, rs.created_at desc
              limit 1
            ) r on true
            left join lateral (
              select ev.fulfillment, ev.disqualified, ev.total_score, ev.created_at
              from app.evaluations ev
              where ev.application_id = a.id and ev.processing_status = 'COMPLETED'
              order by ev.created_at desc
              limit 1
            ) e on true
            where j.company_id = {companyId}
              and j.deleted_at is null
              and a.job_opening_id = any({ids})
            """)
            .ToListAsync(ct);

        return rows.Select(row => new ApplicationFact(
            row.ApplicationId,
            row.JobOpeningId,
            row.CandidateName,
            row.SelectionStatus,
            row.AppliedAt,
            row.ResumeStatus,
            row.ResumeHash,
            row.ResumeCreatedAt,
            row.Fulfillment,
            row.Disqualified,
            row.TotalScore,
            row.EvaluatedAt)).ToList();
    }

    internal sealed class FactRow
    {
        public Guid ApplicationId { get; set; }

        public Guid JobOpeningId { get; set; }

        public string CandidateName { get; set; } = string.Empty;

        public string SelectionStatus { get; set; } = string.Empty;

        public DateTimeOffset AppliedAt { get; set; }

        public string? ResumeStatus { get; set; }

        public string? ResumeHash { get; set; }

        public DateTimeOffset? ResumeCreatedAt { get; set; }

        public string? Fulfillment { get; set; }

        public bool Disqualified { get; set; }

        public decimal? TotalScore { get; set; }

        public DateTimeOffset? EvaluatedAt { get; set; }
    }
}
