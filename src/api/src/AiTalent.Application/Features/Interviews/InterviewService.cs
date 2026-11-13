using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Application.Features.Interviews;

public sealed class InterviewService(IInterviewReadStore interviews, StaffAccess access)
{
    public async Task<Result<IReadOnlyList<InterviewResponse>>> ListAsync(Guid actorUserId, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<IReadOnlyList<InterviewResponse>>.Failure(member.Error!);
        }

        var rows = await interviews.ListByCompanyAsync(member.Value!.CompanyId, ct);
        return Result<IReadOnlyList<InterviewResponse>>.Success(rows.Select(MapInterview).ToList());
    }

    public async Task<Result<InterviewResponse>> GetAsync(Guid actorUserId, Guid interviewId, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<InterviewResponse>.Failure(member.Error!);
        }

        var row = await interviews.FindByIdAsync(member.Value!.CompanyId, interviewId, ct);
        if (row is null)
        {
            return Result<InterviewResponse>.Failure(ErrorKind.NotFound, "Interview not found.");
        }

        return Result<InterviewResponse>.Success(MapInterview(row));
    }

    private static InterviewResponse MapInterview(InterviewSnapshot snapshot)
    {
        var startedAt = snapshot.StartedAt ?? snapshot.CreatedAt;
        var durationMin = snapshot.CompletedAt is { } completed
            ? Math.Max(1, (int)Math.Round((completed - startedAt).TotalMinutes))
            : Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - startedAt).TotalMinutes));

        var turnsWithQuestions = snapshot.Turns.Where(turn => !string.IsNullOrWhiteSpace(turn.Question)).ToList();
        var turns = snapshot.Turns
            .Select(turn => new InterviewTurnResponse(
                turn.TurnOrder,
                string.IsNullOrWhiteSpace(turn.Topic) ? "General" : turn.Topic!,
                turn.Question,
                turn.Answer,
                MapVerdict(turn.AnswerEvaluation)))
            .ToList();

        return new InterviewResponse(
            snapshot.InterviewId,
            snapshot.EvaluationId,
            new CandidateResponse(snapshot.CandidateId, snapshot.CandidateName, snapshot.CandidateEmail),
            snapshot.JobTitle,
            MapStatus(snapshot.Status),
            durationMin,
            startedAt.ToString("o"),
            turnsWithQuestions.Count > 0 ? turnsWithQuestions.Count : turns.Count,
            turns,
            ParseContrasts(snapshot.InconsistenciesJson),
            snapshot.Recommendation,
            snapshot.ApplicationId,
            snapshot.VacancyId,
            snapshot.ExpiresAt?.ToString("o"));
    }

    private static string MapStatus(string status) =>
        status switch
        {
            "COMPLETED" => "DONE",
            "IN_PROGRESS" => "IN_PROGRESS",
            "SCHEDULED" => "SCHEDULED",
            _ => status,
        };

    private static string? MapVerdict(string? answerEvaluation) =>
        answerEvaluation?.ToLowerInvariant() switch
        {
            "match" or "aligned" or "consistent" => "match",
            "conflict" or "inconsistent" or "mismatch" => "conflict",
            _ => null,
        };

    private static IReadOnlyList<InterviewContrastResponse> ParseContrasts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Select(element => new InterviewContrastResponse(
                    element.TryGetProperty("label", out var label) ? label.GetString() ?? string.Empty : string.Empty,
                    element.TryGetProperty("cvSays", out var cvSays) ? cvSays.GetString() ?? "—" : "—",
                    element.TryGetProperty("interviewSays", out var interviewSays) ? interviewSays.GetString() ?? "—" : "—",
                    element.TryGetProperty("verdict", out var verdict) ? verdict.GetString() ?? "missing" : "missing"))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
