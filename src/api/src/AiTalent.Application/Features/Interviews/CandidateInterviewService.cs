using System.Net;
using System.Security.Cryptography;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Portal;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Application.Features.Interviews;

public sealed record InviteToInterviewInput(DateTimeOffset? ExpiresAt, string? Language, bool Notify, string LinkBaseUrl);

public sealed record InterviewInvitationResponse(Guid InterviewId, string Link, string ExpiresAt, int Questions, bool EmailSent);

public sealed record CandidateInterviewResponse(
    string Token,
    string CandidateName,
    string JobTitle,
    string Company,
    string ExpiresAt,
    string State,
    IReadOnlyList<string> Questions,
    int Answered);

public sealed record ScheduleTeamInterviewInput(DateOnly? Date, string? Time, int? DurationMin, IReadOnlyList<string>? Guests);

/// <summary>
/// The AI interview the candidate answers by chat (text only): the recruiter invites from the ranking, the
/// candidate opens the link (a random token, no session) and each answer is stored as it is sent.
/// Questions come from what the evaluation could not confirm, then from the mandatory requirements.
/// </summary>
public sealed class CandidateInterviewService(
    ICandidateSideStore store,
    StaffAccess access,
    IEmailSender email,
    IAuditLog audit,
    TimeProvider clock)
{
    public const int MaxQuestions = 6;
    public const int MaxAnswerLength = 4000;
    private static readonly string[] Languages = ["es", "es-en", "en"];

    public async Task<Result<InterviewInvitationResponse>> InviteAsync(
        Guid actorUserId,
        Guid vacancyId,
        Guid applicationId,
        InviteToInterviewInput input,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<InterviewInvitationResponse>.Failure(member.Error!);
        }

        var target = await store.FindInterviewTargetAsync(member.Value!.CompanyId, vacancyId, applicationId, ct);
        if (target is null)
        {
            return Result<InterviewInvitationResponse>.Failure(ErrorKind.NotFound, "Application not found.");
        }

        if (target.SelectionStatus is "REJECTED" or "WITHDRAWN" or "HIRED")
        {
            return Result<InterviewInvitationResponse>.Failure(ErrorKind.Conflict, "This application is closed.");
        }

        if (target.EvaluationId is not { } evaluationId)
        {
            return Result<InterviewInvitationResponse>.Failure(ErrorKind.Conflict, "Evaluate the CV before inviting to an interview.");
        }

        var language = string.IsNullOrWhiteSpace(input.Language) ? "es" : input.Language.Trim().ToLowerInvariant();
        if (!Languages.Contains(language))
        {
            return Result<InterviewInvitationResponse>.Failure(ErrorKind.Invalid, "Language must be es, es-en or en.");
        }

        var now = clock.GetUtcNow();
        var expiresAt = input.ExpiresAt ?? now.AddDays(3);
        if (expiresAt <= now || expiresAt > now.AddDays(30))
        {
            return Result<InterviewInvitationResponse>.Failure(ErrorKind.Invalid, "The deadline must be within the next 30 days.");
        }

        var questions = BuildQuestions(await store.ListRequirementGapsAsync(evaluationId, ct), language);
        if (questions.Count == 0)
        {
            return Result<InterviewInvitationResponse>.Failure(ErrorKind.Invalid, "The vacancy profile has no requirements to ask about.");
        }

        var token = NewToken();
        var interviewId = await store.CreateInterviewAsync(evaluationId, applicationId, actorUserId, token, expiresAt, language, questions, ct);
        var link = $"{input.LinkBaseUrl.TrimEnd('/')}/interview/{token}";

        var sent = false;
        if (input.Notify)
        {
            sent = await TrySendAsync(new EmailMessage(
                target.CandidateEmail,
                $"{target.CompanyName} te invita a una entrevista corta · {target.VacancyTitle}",
                Html(
                    $"Hola, {FirstName(target.CandidateName)}",
                    $"{target.CompanyName} quiere conocerte mejor para la vacante <strong>{WebUtility.HtmlEncode(target.VacancyTitle)}</strong>. Son {questions.Count} preguntas por chat sobre tu experiencia; toma unos 15 minutos y la respondes cuando te quede bien.",
                    link,
                    "Responder la entrevista",
                    $"El enlace vence el {Dates.Day(expiresAt)}."),
                $"{target.CompanyName} te invita a una entrevista por chat para {target.VacancyTitle}: {link} (vence el {Dates.Day(expiresAt)}).",
                target.CandidateName), ct);
        }

        await audit.RecordAsync(
            actorUserId,
            "application.interview.invited",
            "application",
            applicationId.ToString(),
            new { summary = "Invitó a entrevista con IA", candidate = target.CandidateName, questions = questions.Count, emailSent = sent },
            ct);

        return Result<InterviewInvitationResponse>.Success(new InterviewInvitationResponse(interviewId, link, Dates.Day(expiresAt)!, questions.Count, sent));
    }

    public async Task<Result<CandidateInterviewResponse>> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        var interview = await FindAsync(token, ct);
        if (interview is null)
        {
            return Result<CandidateInterviewResponse>.Failure(ErrorKind.NotFound, "Interview not found.");
        }

        var now = clock.GetUtcNow();
        var state = interview.Status switch
        {
            "COMPLETED" => "done",
            "CANCELLED" => "closed",
            _ when interview.ExpiresAt is { } expires && expires <= now => "closed",
            _ => "open",
        };

        return Result<CandidateInterviewResponse>.Success(new CandidateInterviewResponse(
            token,
            FirstName(interview.CandidateName),
            interview.JobTitle,
            interview.Company,
            (interview.ExpiresAt ?? now.AddDays(3)).ToString("o"),
            state,
            interview.Turns.Select(turn => turn.Question).ToList(),
            interview.Turns.Count(turn => !string.IsNullOrWhiteSpace(turn.Answer))));
    }

    public async Task<Result<bool>> AnswerAsync(string token, int order, string? answer, CancellationToken ct = default)
    {
        var text = answer?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxAnswerLength)
        {
            return Result<bool>.Failure(ErrorKind.Invalid, $"The answer must have between 1 and {MaxAnswerLength} characters.");
        }

        var interview = await FindAsync(token, ct);
        if (interview is null)
        {
            return Result<bool>.Failure(ErrorKind.NotFound, "Interview not found.");
        }

        if (interview.Status is "COMPLETED" or "CANCELLED" || interview.ExpiresAt is { } expires && expires <= clock.GetUtcNow())
        {
            return Result<bool>.Failure(ErrorKind.Conflict, "This interview is closed.");
        }

        return await store.SaveAnswerAsync(interview.InterviewId, order, text, ct)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(ErrorKind.NotFound, "Question not found.");
    }

    /// <summary>The human interview after the AI one: e-mails the candidate and the guests and leaves it in the audit log.</summary>
    public async Task<Result<bool>> ScheduleWithTeamAsync(Guid actorUserId, Guid interviewId, ScheduleTeamInterviewInput input, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<bool>.Failure(member.Error!);
        }

        if (input.Date is not { } date || date <= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) || string.IsNullOrWhiteSpace(input.Time))
        {
            return Result<bool>.Failure(ErrorKind.Invalid, "Choose a future date and a time.");
        }

        var guests = (input.Guests ?? []).Select(guest => guest.Trim()).Where(guest => guest.Length > 0).ToList();
        if (guests.Count > 10 || guests.Any(guest => !Validation.IsEmail(guest)))
        {
            return Result<bool>.Failure(ErrorKind.Invalid, "Guests must be up to 10 valid e-mail addresses.");
        }

        var contact = await store.FindInterviewContactAsync(member.Value!.CompanyId, interviewId, ct);
        if (contact is null)
        {
            return Result<bool>.Failure(ErrorKind.NotFound, "Interview not found.");
        }

        var when = $"{date:dd/MM/yyyy} a las {input.Time} ({input.DurationMin ?? 45} minutos)";
        var body = Html(
            $"Hola, {FirstName(contact.CandidateName)}",
            $"{WebUtility.HtmlEncode(contact.CompanyName)} quiere seguir el proceso de <strong>{WebUtility.HtmlEncode(contact.JobTitle)}</strong> con una conversación con el equipo el {when}. Te escribirán con el enlace de la reunión.",
            null,
            null,
            null);
        await TrySendAsync(new EmailMessage(contact.CandidateEmail, $"Entrevista con el equipo · {contact.JobTitle}", body, null, contact.CandidateName), ct);
        foreach (var guest in guests)
        {
            await TrySendAsync(new EmailMessage(guest, $"Entrevista con {contact.CandidateName} · {contact.JobTitle}", Html("Hola", $"Quedó programada la entrevista con {WebUtility.HtmlEncode(contact.CandidateName)} para {WebUtility.HtmlEncode(contact.JobTitle)} el {when}.", null, null, null)), ct);
        }

        await audit.RecordAsync(
            actorUserId,
            "application.interview.team_scheduled",
            "application",
            contact.ApplicationId.ToString(),
            new { summary = $"Programó entrevista con el equipo el {when}", candidate = contact.CandidateName },
            ct);
        return Result<bool>.Success(true);
    }

    internal static List<InterviewQuestionSeed> BuildQuestions(IReadOnlyList<RequirementGapRow> requirements, string language)
    {
        // What the evaluation could not confirm goes first; then the mandatory ones it did confirm.
        var ordered = requirements
            .OrderBy(requirement => requirement.Result switch
            {
                "PARTIALLY_MEETS" => 0,
                "NO_EVIDENCE" or null => 1,
                "DOES_NOT_MEET" => 2,
                _ => requirement.Type == "MANDATORY" ? 3 : 4,
            })
            .Take(MaxQuestions)
            .ToList();

        var english = language switch
        {
            "en" => ordered.Count,
            "es-en" => Math.Min(2, ordered.Count),
            _ => 0,
        };

        return ordered.Select((requirement, index) =>
        {
            var topic = requirement.Category is { Length: > 0 } category ? category : Shorten(requirement.Description);
            var inEnglish = index >= ordered.Count - english;
            var question = (inEnglish, requirement.Result) switch
            {
                (true, _) => $"In English, please: tell me about a project where you worked with “{requirement.Description}”. What was your role and what was the result?",
                (false, "PARTIALLY_MEETS") => $"En tu hoja de vida aparece algo de «{requirement.Description}», pero no del todo. Cuéntame un caso concreto: qué hiciste, con qué herramientas y qué resultado tuvo.",
                (false, "MEETS") => $"Sobre «{requirement.Description}»: cuéntame el proyecto más exigente en el que lo aplicaste y cuál fue tu papel.",
                _ => $"Tu hoja de vida no menciona «{requirement.Description}». ¿Tienes experiencia con esto o con algo parecido? Cuéntame dónde y cómo.",
            };
            return new InterviewQuestionSeed(topic, question);
        }).ToList();
    }

    private async Task<PublicInterviewRow?> FindAsync(string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 64 ? null : await store.FindInterviewByTokenAsync(token, ct);

    private async Task<bool> TrySendAsync(EmailMessage message, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(message, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SMTP may be unconfigured in development: the invitation still exists and the recruiter can copy the link.
            return false;
        }
    }

    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..37].TrimEnd() + "…";

    internal static string Html(string greeting, string paragraph, string? link, string? button, string? footnote) =>
        $"""
        <div style="font-family:Arial,sans-serif;max-width:520px;margin:0 auto;color:#16201b">
          <p style="font-size:16px">{WebUtility.HtmlEncode(greeting)}</p>
          <p style="font-size:15px;line-height:1.5">{paragraph}</p>
          {(link is null ? string.Empty : $"<p style=\"margin:28px 0\"><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"background:#16201b;color:#fff;padding:12px 20px;border-radius:999px;text-decoration:none;font-weight:600\">{WebUtility.HtmlEncode(button ?? "Abrir")}</a></p>")}
          {(footnote is null ? string.Empty : $"<p style=\"font-size:13px;color:#5b6660\">{WebUtility.HtmlEncode(footnote)}</p>")}
          <p style="font-size:12px;color:#8a948e">Criba</p>
        </div>
        """;
}
