using System.Text.Json;
using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class EvaluationReadStore(AppDbContext db) : IEvaluationReadStore
{
    public async Task<IReadOnlyList<VacancyEvaluationStats>> ListOpenVacancyStatsAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            select
              jo.id,
              jo.title,
              jo.closing_at,
              coalesce(rp.version, 0) as profile_version,
              count(distinct a.id) filter (where a.selection_status not in ('WITHDRAWN')) as received,
              count(distinct e.id) filter (where e.fulfillment = 'MEETS' and e.disqualified = false) as meets,
              count(distinct e.id) filter (where e.fulfillment = 'PARTIALLY_MEETS' and e.disqualified = false) as partial,
              count(distinct e.id) filter (where e.fulfillment = 'DOES_NOT_MEET' or e.disqualified) as does_not_meet
            from app.job_openings jo
            left join lateral (
              select id, version
              from app.role_profiles
              where job_opening_id = jo.id
              order by version desc
              limit 1
            ) rp on true
            left join app.applications a on a.job_opening_id = jo.id
            left join lateral (
              select e2.*
              from app.evaluations e2
              where e2.application_id = a.id
                and e2.role_profile_id = rp.id
                and e2.processing_status = 'COMPLETED'
              order by e2.created_at desc
              limit 1
            ) e on a.id is not null
            where jo.company_id = @company_id
              and jo.deleted_at is null
              and jo.status = 'OPEN'
            group by jo.id, jo.title, jo.closing_at, rp.version
            order by jo.title
            """;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "company_id";
            parameter.Value = companyId;
            command.Parameters.Add(parameter);

            var rows = new List<VacancyEvaluationStats>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new VacancyEvaluationStats(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    await reader.IsDBNullAsync(2, ct) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7)));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<IReadOnlyList<VacancyEvaluationSnapshot>> ListLatestCompletedAsync(
        Guid vacancyId,
        Guid roleProfileId,
        CancellationToken ct = default)
    {
        const string evalSql = """
            select distinct on (a.id)
              a.id,
              c.id,
              c.name,
              a.source,
              e.id,
              e.resume_id,
              e.total_score,
              e.fulfillment,
              e.disqualified,
              e.confidence,
              e.created_at,
              r.professional_data::text
            from app.applications a
            join app.candidates c on c.id = a.candidate_id
            join app.evaluations e on e.application_id = a.id
            join app.resumes r on r.id = e.resume_id
            where a.job_opening_id = @vacancy_id
              and e.role_profile_id = @profile_id
              and e.processing_status = 'COMPLETED'
              and a.selection_status not in ('WITHDRAWN')
            order by a.id, e.created_at desc
            """;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = evalSql;
            var vacancyParam = command.CreateParameter();
            vacancyParam.ParameterName = "vacancy_id";
            vacancyParam.Value = vacancyId;
            command.Parameters.Add(vacancyParam);
            var profileParam = command.CreateParameter();
            profileParam.ParameterName = "profile_id";
            profileParam.Value = roleProfileId;
            command.Parameters.Add(profileParam);

            var baseRows = new List<(Guid ApplicationId, Guid CandidateId, string Name, string Source, Guid EvaluationId, Guid ResumeId, decimal Score, string Fulfillment, bool Disqualified, decimal Confidence, DateTimeOffset EvaluatedAt, string? ProfileJson)>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    baseRows.Add((
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetGuid(4),
                        reader.GetGuid(5),
                        reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                        reader.IsDBNull(7) ? "DOES_NOT_MEET" : reader.GetString(7),
                        !reader.IsDBNull(8) && reader.GetBoolean(8),
                        reader.IsDBNull(9) ? 0m : reader.GetDecimal(9),
                        reader.GetFieldValue<DateTimeOffset>(10),
                        await reader.IsDBNullAsync(11, ct) ? null : reader.GetString(11)));
                }
            }

            if (baseRows.Count == 0)
            {
                return [];
            }

            var evaluationIds = baseRows.Select(row => row.EvaluationId).ToArray();
            var results = await LoadRequirementResultsAsync(evaluationIds, ct);

            return baseRows
                .Select(row => new VacancyEvaluationSnapshot(
                    row.ApplicationId,
                    row.CandidateId,
                    row.Name,
                    row.Source,
                    row.ResumeId,
                    row.EvaluationId,
                    row.Score,
                    row.Fulfillment,
                    row.Disqualified,
                    row.Confidence,
                    row.EvaluatedAt,
                    row.ProfileJson,
                    results.TryGetValue(row.EvaluationId, out var list) ? list : []))
                .ToList();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<GovernanceEvaluationStats> GetGovernanceEvaluationStatsAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            with company_evaluations as (
              select e.id
              from app.evaluations e
              join app.applications a on a.id = e.application_id
              join app.job_openings jo on jo.id = a.job_opening_id
              where jo.company_id = @company_id
                and jo.deleted_at is null
                and e.processing_status = 'COMPLETED'
            ),
            requirement_totals as (
              select count(*)::int as total
              from app.requirement_results rr
              where rr.evaluation_id in (select id from company_evaluations)
            ),
            with_evidence as (
              select count(distinct rr.id)::int as total
              from app.requirement_results rr
              join app.evidence ev on ev.requirement_result_id = rr.id
              where rr.evaluation_id in (select id from company_evaluations)
            )
            select
              (select count(*)::int from company_evaluations),
              case
                when (select total from requirement_totals) = 0 then 100
                else round(100.0 * (select total from with_evidence) / (select total from requirement_totals))::int
              end
            """;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "company_id";
            parameter.Value = companyId;
            command.Parameters.Add(parameter);

            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new GovernanceEvaluationStats(0, 100, 0);
            }

            return new GovernanceEvaluationStats(reader.GetInt32(0), reader.GetInt32(1), 0);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<Dictionary<Guid, List<EvaluationRequirementSnapshot>>> LoadRequirementResultsAsync(
        Guid[] evaluationIds,
        CancellationToken ct)
    {
        if (evaluationIds.Length == 0)
        {
            return [];
        }

        const string sql = """
            select
              rr.evaluation_id,
              rr.id,
              rr.requirement_id,
              rr.result,
              rr.score,
              rr.confidence,
              rr.justification,
              ev.id,
              ev.citation,
              rf.source_location::text
            from app.requirement_results rr
            left join app.evidence ev on ev.requirement_result_id = rr.id
            left join app.resume_fragments rf on rf.id = ev.resume_fragment_id
            where rr.evaluation_id = any(@evaluation_ids)
            order by rr.evaluation_id, rr.requirement_id, ev.id
            """;

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "evaluation_ids";
        parameter.Value = evaluationIds;
        command.Parameters.Add(parameter);

        var grouped = new Dictionary<Guid, Dictionary<Guid, EvaluationRequirementSnapshot>>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var evaluationId = reader.GetGuid(0);
            var resultId = reader.GetGuid(1);
            var requirementId = reader.GetGuid(2);

            if (!grouped.TryGetValue(evaluationId, out var byRequirement))
            {
                byRequirement = [];
                grouped[evaluationId] = byRequirement;
            }

            if (!byRequirement.TryGetValue(requirementId, out var snapshot))
            {
                snapshot = new EvaluationRequirementSnapshot(
                    resultId,
                    requirementId,
                    reader.GetString(3),
                    reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                    reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                    []);
                byRequirement[requirementId] = snapshot;
            }

            if (!await reader.IsDBNullAsync(7, ct))
            {
                var evidence = new EvaluationEvidenceSnapshot(
                    reader.GetGuid(7),
                    reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    await reader.IsDBNullAsync(9, ct) ? null : FormatLocation(reader.GetString(9)));
                var evidenceList = snapshot.Evidence.ToList();
                evidenceList.Add(evidence);
                byRequirement[requirementId] = snapshot with { Evidence = evidenceList };
            }
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Values.ToList());
    }

    private static string? FormatLocation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("page", out var page) && page.TryGetInt32(out var pageNumber))
            {
                return $"CV pág. {pageNumber}";
            }
        }
        catch (JsonException)
        {
            // Ignore malformed source_location payloads.
        }

        return null;
    }
}
