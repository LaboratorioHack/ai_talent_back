using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Dashboard;
using AiTalent.Application.Features.Portal;
using AiTalent.Application.Features.Rankings;
using AiTalent.Application.Features.Staff;
using AiTalent.Application.Features.Vacancies;

namespace AiTalent.Application.Features.Assistant;

public sealed record AssistantMessage(string? Role, string? Content);

public sealed record AssistantReply(string Reply);

/// <summary>
/// Criba AI as a chat in both portals. The API decides what each person may see and sends only that
/// as context: a recruiter gets their organization's vacancies, figures and (on a vacancy page) its
/// requirements and ranking; an applicant gets only their own applications, résumé and open jobs.
/// Each question leaves an audit entry (never the text of the conversation).
/// </summary>
public sealed class AssistantService(
    IAiEngineClient engine,
    StaffAccess access,
    DashboardService dashboard,
    VacancyService vacancies,
    RankingService rankings,
    PortalService portal,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int MaxMessages = 20;
    public const int MaxMessageLength = 2000;
    public const int MaxOpenJobs = 15;

    public async Task<Result<AssistantReply>> AskAsCompanyAsync(
        Actor actor,
        IReadOnlyList<AssistantMessage>? messages,
        Guid? vacancyId,
        CancellationToken ct = default)
    {
        var turns = Clean(messages);
        if (!turns.IsSuccess)
        {
            return Result<AssistantReply>.Failure(turns.Error!);
        }

        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<AssistantReply>.Failure(member.Error!);
        }

        var board = await dashboard.GetDashboardAsync(actor, ct);
        object? vacancy = null;
        object? ranking = null;
        if (vacancyId is { } id)
        {
            var detail = await vacancies.GetAsync(actor.UserId, id, ct);
            vacancy = detail.IsSuccess ? detail.Value : null;
            var ranked = await rankings.GetByVacancyAsync(actor.UserId, id, ct);
            ranking = ranked.IsSuccess ? ranked.Value : null;
        }

        var context = new
        {
            today = Dates.Day(clock.GetUtcNow()),
            page = vacancy is null ? "panel" : "vacancy",
            dashboard = board.IsSuccess ? board.Value : null,
            vacancy,
            ranking,
        };

        var call = await AskEngineAsync("company", context, turns.Value!, ct);
        if (call.IsSuccess)
        {
            await audit.RecordAsync(
                actor.UserId,
                "assistant.asked",
                "company",
                member.Value!.CompanyId.ToString(),
                new { audience = "company", vacancyId, turns = turns.Value!.Count },
                ct);
        }

        return call;
    }

    public async Task<Result<AssistantReply>> AskAsApplicantAsync(
        Guid userId,
        string email,
        string? sessionName,
        IReadOnlyList<AssistantMessage>? messages,
        CancellationToken ct = default)
    {
        var turns = Clean(messages);
        if (!turns.IsSuccess)
        {
            return Result<AssistantReply>.Failure(turns.Error!);
        }

        var resume = await portal.GetResumeAsync(email, ct);
        var context = new
        {
            today = Dates.Day(clock.GetUtcNow()),
            applicant = await portal.GetApplicantAsync(email, sessionName, ct),
            applications = await portal.ListApplicationsAsync(email, ct),
            resume = resume.IsSuccess ? resume.Value : null,
            openJobs = (await portal.ListOpenJobsAsync(email, ct)).Take(MaxOpenJobs),
        };

        var call = await AskEngineAsync("applicant", context, turns.Value!, ct);
        if (call.IsSuccess)
        {
            await audit.RecordAsync(
                userId,
                "assistant.asked",
                "applicant",
                userId.ToString(),
                new { audience = "applicant", turns = turns.Value!.Count },
                ct);
        }

        return call;
    }

    private async Task<Result<AssistantReply>> AskEngineAsync(
        string audience,
        object context,
        IReadOnlyList<AssistantTurnDto> turns,
        CancellationToken ct)
    {
        var call = await EngineCall.RunAsync(
            () => engine.ChatAsync(new AssistantChatRequest(audience, context, turns), ct),
            "Criba AI could not answer that message.",
            ct);
        return call.IsSuccess
            ? Result<AssistantReply>.Success(new AssistantReply(call.Value!.Reply))
            : Result<AssistantReply>.Failure(call.Error!);
    }

    /// <summary>1 to 20 turns of user/assistant text, the last one from the user, each up to 2000 characters.</summary>
    internal static Result<IReadOnlyList<AssistantTurnDto>> Clean(IReadOnlyList<AssistantMessage>? messages)
    {
        if (messages is null || messages.Count is 0 or > MaxMessages)
        {
            return Result<IReadOnlyList<AssistantTurnDto>>.Failure(ErrorKind.Invalid, $"Send between 1 and {MaxMessages} messages.");
        }

        var turns = new List<AssistantTurnDto>(messages.Count);
        foreach (var message in messages)
        {
            var role = message.Role?.Trim().ToLowerInvariant();
            var content = message.Content?.Trim() ?? string.Empty;
            if (role is not ("user" or "assistant") || content.Length is 0 or > MaxMessageLength)
            {
                return Result<IReadOnlyList<AssistantTurnDto>>.Failure(
                    ErrorKind.Invalid,
                    $"Each message needs a role (user or assistant) and 1 to {MaxMessageLength} characters.");
            }

            turns.Add(new AssistantTurnDto(role, content));
        }

        return turns[^1].Role == "user"
            ? Result<IReadOnlyList<AssistantTurnDto>>.Success(turns)
            : Result<IReadOnlyList<AssistantTurnDto>>.Failure(ErrorKind.Invalid, "The last message must come from the user.");
    }
}
