using AiTalent.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class InterviewReadStore(AppDbContext db) : IInterviewReadStore
{
    public async Task<IReadOnlyList<InterviewSnapshot>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        var headers = await LoadHeadersAsync(companyId, interviewId: null, ct);
        return await AttachTurnsAsync(headers, ct);
    }

    public async Task<InterviewSnapshot?> FindByIdAsync(Guid companyId, Guid interviewId, CancellationToken ct = default)
    {
        var headers = await LoadHeadersAsync(companyId, interviewId, ct);
        if (headers.Count == 0)
        {
            return null;
        }

        var list = await AttachTurnsAsync(headers, ct);
        return list[0];
    }

    private async Task<List<InterviewSnapshot>> LoadHeadersAsync(
        Guid companyId,
        Guid? interviewId,
        CancellationToken ct)
    {
        const string sql = """
            select
              i.id,
              i.evaluation_id,
              c.id,
              c.name,
              c.contact_email,
              jo.title,
              i.status,
              i.started_at,
              i.created_at,
              i.completed_at,
              i.recommendation,
              i.inconsistencies::text
            from app.interviews i
            join app.evaluations e on e.id = i.evaluation_id
            join app.applications a on a.id = e.application_id
            join app.candidates c on c.id = a.candidate_id
            join app.job_openings jo on jo.id = a.job_opening_id
            where jo.company_id = @company_id
              and jo.deleted_at is null
              and (@interview_id is null or i.id = @interview_id)
            order by coalesce(i.started_at, i.created_at) desc
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
            var interviewParam = command.CreateParameter();
            interviewParam.ParameterName = "interview_id";
            interviewParam.Value = (object?)interviewId ?? DBNull.Value;
            command.Parameters.Add(interviewParam);

            var rows = new List<InterviewSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new InterviewSnapshot(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    await reader.IsDBNullAsync(7, ct) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetFieldValue<DateTimeOffset>(8),
                    await reader.IsDBNullAsync(9, ct) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                    await reader.IsDBNullAsync(10, ct) ? null : reader.GetString(10),
                    await reader.IsDBNullAsync(11, ct) ? null : reader.GetString(11),
                    []));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<IReadOnlyList<InterviewSnapshot>> AttachTurnsAsync(
        IReadOnlyList<InterviewSnapshot> headers,
        CancellationToken ct)
    {
        if (headers.Count == 0)
        {
            return headers;
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var interviewIds = headers.Select(header => header.InterviewId).ToArray();
            const string sql = """
            select interview_id, turn_order, topic, question, answer, answer_evaluation
            from app.interview_turns
            where interview_id = any(@interview_ids)
            order by interview_id, turn_order
            """;

            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "interview_ids";
            parameter.Value = interviewIds;
            command.Parameters.Add(parameter);

            var turnsByInterview = new Dictionary<Guid, List<InterviewTurnSnapshot>>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var interviewId = reader.GetGuid(0);
                if (!turnsByInterview.TryGetValue(interviewId, out var turns))
                {
                    turns = [];
                    turnsByInterview[interviewId] = turns;
                }

                turns.Add(new InterviewTurnSnapshot(
                    reader.GetInt32(1),
                    await reader.IsDBNullAsync(2, ct) ? null : reader.GetString(2),
                    reader.GetString(3),
                    await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4),
                    await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5)));
            }

            return headers
                .Select(header => header with
                {
                    Turns = turnsByInterview.TryGetValue(header.InterviewId, out var turns) ? turns : [],
                })
                .ToList();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
