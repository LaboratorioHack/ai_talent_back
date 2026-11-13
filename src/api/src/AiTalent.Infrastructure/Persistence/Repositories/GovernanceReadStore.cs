using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class GovernanceReadStore(AppDbContext db) : IGovernanceReadStore
{
    public async Task<IReadOnlyList<AuditLogEntry>> ListForCompanyAsync(Guid companyId, int limit, DateTimeOffset since, CancellationToken ct = default)
    {
        const string sql = """
            select
              al.id,
              al.occurred_at,
              al.actor_id,
              al.actor_type,
              al.action,
              al.entity,
              al.entity_id,
              al.details::text,
              iv.name,
              coalesce(
                (select jo.title from app.job_openings jo where al.entity = 'job_opening' and jo.id::text = al.entity_id),
                (select c.name from app.evaluations e
                   join app.applications a on a.id = e.application_id
                   join app.candidates c on c.id = a.candidate_id
                 where al.entity = 'evaluation' and e.id::text = al.entity_id),
                (select c.name from app.applications a
                   join app.candidates c on c.id = a.candidate_id
                 where al.entity = 'application' and a.id::text = al.entity_id)
              ) as subject_name
            from app.audit_log al
            left join app.interviewers iv on iv.user_id = al.actor_id
            where (
              iv.company_id = @company_id
              or (
                al.actor_type in ('engine', 'api', 'agent')
                and al.entity = 'job_opening'
                and exists (
                  select 1
                  from app.job_openings jo
                  where jo.company_id = @company_id
                    and jo.id::text = al.entity_id
                )
              )
              or (
                al.actor_type in ('engine', 'api', 'agent')
                and al.entity = 'evaluation'
                and exists (
                  select 1
                  from app.evaluations e
                  join app.applications a on a.id = e.application_id
                  join app.job_openings jo on jo.id = a.job_opening_id
                  where jo.company_id = @company_id
                    and e.id::text = al.entity_id
                )
              )
            )
              and al.occurred_at >= @since
            order by al.occurred_at desc
            limit @limit
            """;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var companyParam = command.CreateParameter();
            companyParam.ParameterName = "company_id";
            companyParam.Value = companyId;
            command.Parameters.Add(companyParam);
            var limitParam = command.CreateParameter();
            limitParam.ParameterName = "limit";
            limitParam.Value = limit;
            command.Parameters.Add(limitParam);
            var sinceParam = command.CreateParameter();
            sinceParam.ParameterName = "since";
            sinceParam.Value = since;
            command.Parameters.Add(sinceParam);

            var rows = new List<AuditLogEntry>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new AuditLogEntry(
                    reader.GetInt64(0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    await reader.IsDBNullAsync(2, ct) ? null : reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? "{}" : reader.GetString(7),
                    await reader.IsDBNullAsync(8, ct) ? null : reader.GetString(8),
                    await reader.IsDBNullAsync(9, ct) ? null : reader.GetString(9)));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<int> CountHumanDecisionsAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default)
    {
        const string sql = """
            select count(*)::int
            from app.audit_log al
            join app.interviewers iv on iv.user_id = al.actor_id
            where iv.company_id = @company_id
              and al.actor_type = 'user'
              and al.occurred_at >= @since
            """;

        return await ScalarIntAsync(sql, companyId, since, ct);
    }

    public async Task<int> CountAiErrorsFlaggedAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default)
    {
        const string sql = """
            select count(*)::int
            from app.audit_log al
            join app.interviewers iv on iv.user_id = al.actor_id
            where iv.company_id = @company_id
              and al.actor_type = 'user'
              and (
                al.action ilike '%false_positive%'
                or al.action ilike '%flagged%'
                or al.action ilike '%corrected%'
              )
              and al.occurred_at >= @since
            """;

        return await ScalarIntAsync(sql, companyId, since, ct);
    }

    private async Task<int> ScalarIntAsync(string sql, Guid companyId, DateTimeOffset since, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "company_id";
            parameter.Value = companyId;
            command.Parameters.Add(parameter);
            var sinceParam = command.CreateParameter();
            sinceParam.ParameterName = "since";
            sinceParam.Value = since;
            command.Parameters.Add(sinceParam);

            var value = await command.ExecuteScalarAsync(ct);
            return value is int i ? i : Convert.ToInt32(value);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
