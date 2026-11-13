using System.Security.Claims;
using AiTalent.Application.Features.Candidates;
using AiTalent.Application.Features.Interviews;
using AiTalent.Application.Features.Portal;

namespace AiTalent.Api.Endpoints;

internal sealed record AnswerTurnRequest(int Order, string? Answer);

internal sealed record InviteToInterviewRequest(DateTimeOffset? ExpiresAt, string? Language, bool? Notify);

internal sealed record ScheduleTeamInterviewRequest(DateOnly? Date, string? Time, int? DurationMin, IReadOnlyList<string>? Guests);

internal sealed record AiErrorRequest(string? Kind, Guid? RequirementId, string? Note);

internal sealed record PortalLinkRequest(string? Email);

/// <summary>
/// The candidate side: the public board and the interview link (no session), the applicant portal
/// (magic-link session, identified by its e-mail) and the recruiter actions that reach the candidate.
/// </summary>
internal static class CandidateSideEndpoints
{
    public static IEndpointRouteBuilder MapCandidateSideEndpoints(this IEndpointRouteBuilder app)
    {
        var publicApi = app.MapGroup("/api/public").AllowAnonymous().RequireRateLimiting(RateLimitPolicies.PublicRead);

        publicApi.MapGet("vacancies", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                Results.Ok(await service.ListOpenJobsAsync(user.Email(), ct)))
            .WithName("ListPublicVacancies")
            .WithSummary("Open vacancies of every company (public)")
            .WithDescription("Board of open vacancies: title, company, area, location, modality, dates and up to three mandatory requirements. With an applicant session it also says whether the person already applied and how close each vacancy is to their CV (`match`: alta, media or baja with the skills that match; never a score).")
            .Produces<IReadOnlyList<PortalJobResponse>>(StatusCodes.Status200OK);

        publicApi.MapGet("interviews/{token}", async (string token, CandidateInterviewService service, CancellationToken ct) =>
                (await service.GetByTokenAsync(token, ct)).ToHttpResult(Results.Ok))
            .WithName("GetCandidateInterview")
            .WithSummary("Open an AI interview by its link (public)")
            .WithDescription("The token of the invitation link is the only credential. `state` is open, done or closed (expired or cancelled). Returns the candidate's first name, the job, the questions and how many were answered.")
            .Produces<CandidateInterviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/public/interviews/{token}/turns", async (string token, AnswerTurnRequest request, CandidateInterviewService service, CancellationToken ct) =>
                (await service.AnswerAsync(token, request.Order, request.Answer, ct)).ToHttpResult(_ => Results.NoContent()))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.CandidateApply)
            .WithName("AnswerCandidateInterview")
            .WithSummary("Answer a question of the AI interview (public)")
            .WithDescription("Stores the answer of question `order` (1-based). A second answer to the same question (the follow-up) is appended. The interview moves to IN_PROGRESS with the first answer and to COMPLETED when every question has one.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/public/portal-link", async (PortalLinkRequest request, HttpRequest http, IConfiguration configuration, IHostEnvironment environment, PortalLinkService service, CancellationToken ct) =>
                (await service.SendAsync(request.Email, TrustedAppOrigin(http, configuration, environment), ct)).ToHttpResult(_ => Results.NoContent()))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.CandidateApply)
            .WithName("SendPortalLink")
            .WithSummary("E-mail the applicant portal sign-in link (public)")
            .WithDescription("Creates a one-time Supabase magic-link token and e-mails it through Criba's SMTP, so it does not depend on Supabase's shared e-mail limit. The link opens `/auth/confirm` on the web app. Always 204 for a valid e-mail; it never reveals whether an account existed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var me = app.MapGroup("/api/me");

        me.MapGet("applicant", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? Results.Ok(await service.GetApplicantAsync(email, user.SessionName(), ct)) : NoEmail())
            .WithName("GetApplicant")
            .WithSummary("The applicant behind the session")
            .Produces<ApplicantResponse>(StatusCodes.Status200OK);

        me.MapGet("applications", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? Results.Ok(await service.ListApplicationsAsync(email, ct)) : NoEmail())
            .WithName("ListMyApplications")
            .WithSummary("My applications")
            .WithDescription("Every application made with the session's e-mail, in any company, with its stage (received, review, interview, decision), outcome (open, selected, closed, withdrawn), a note for the person and the pending interview link. Never scores.")
            .Produces<IReadOnlyList<PortalApplicationResponse>>(StatusCodes.Status200OK);

        me.MapGet("applications/{id:guid}", async (Guid id, ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? (await service.GetApplicationAsync(email, id, ct)).ToHttpResult(Results.Ok) : NoEmail())
            .WithName("GetMyApplication")
            .Produces<PortalApplicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("applications/{id:guid}/withdraw", async (Guid id, ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? (await service.WithdrawAsync(user.ToUserId(), email, id, ct)).ToHttpResult(Results.Ok) : NoEmail())
            .WithName("WithdrawMyApplication")
            .WithSummary("Withdraw one of my applications")
            .Produces<PortalApplicationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapGet("resume", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? (await service.GetResumeAsync(email, ct)).ToHttpResult(Results.Ok) : NoEmail())
            .WithName("GetMyResume")
            .WithSummary("My latest CV as a document, with the skills Criba AI found")
            .Produces<PortalResumeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("resume/file", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? (await service.GetResumeFileAsync(email, ct)).ToHttpResult(file => Results.File(file.Content, file.ContentType)) : NoEmail())
            .WithName("GetMyResumeFile")
            .WithSummary("The file of my latest CV")
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("resume", async (HttpRequest request, ClaimsPrincipal user, ApplicantResumeService service, CancellationToken ct) =>
                {
                    if (user.Email() is not { } email)
                    {
                        return NoEmail();
                    }

                    if (!request.HasFormContentType)
                    {
                        return Results.Problem(detail: "A multipart form with the CV file is required.", statusCode: StatusCodes.Status400BadRequest);
                    }

                    var form = await request.ReadFormAsync(ct);
                    if (form.Files.GetFile("file") is not { } file)
                    {
                        return Results.Problem(detail: "A CV file is required (PDF or DOCX).", statusCode: StatusCodes.Status400BadRequest);
                    }

                    var command = new UploadResumeCommand(file.FileName, file.ContentType, file.Length, file.OpenReadStream);
                    return (await service.UploadAsync(user.ToUserId(), email, command, ct)).ToHttpResult(Results.Ok);
                })
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicies.CandidateApply)
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("UploadMyResume")
            .WithSummary("Replace my CV")
            .WithDescription("Stores a new version of the person's CV (PDF or DOCX, max 10 MB) in every company they applied to, and Criba AI reads it again. Later evaluations use the new version.")
            .Produces<UploadResumeReceipt>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("export", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                user.Email() is { } email ? Results.Ok(await service.ExportAsync(email, ct)) : NoEmail())
            .WithName("ExportMyData")
            .WithSummary("Everything Criba stores about me (Habeas Data)");

        me.MapDelete("", async (ClaimsPrincipal user, PortalService service, CancellationToken ct) =>
                {
                    if (user.Email() is not { } email)
                    {
                        return NoEmail();
                    }

                    await service.EraseAsync(user.ToUserId(), email, ct);
                    return Results.NoContent();
                })
            .WithName("EraseMyData")
            .WithSummary("Erase my CVs, applications, evaluations and interviews (Habeas Data)")
            .Produces(StatusCodes.Status204NoContent);

        var vacancies = app.MapGroup("/api/vacancies");

        vacancies.MapPost("{id:guid}/applications/{applicationId:guid}/interview", async (
                Guid id,
                Guid applicationId,
                InviteToInterviewRequest request,
                HttpRequest http,
                IConfiguration configuration,
                IHostEnvironment environment,
                ClaimsPrincipal user,
                CandidateInterviewService service,
                CancellationToken ct) =>
            {
                var input = new InviteToInterviewInput(request.ExpiresAt, request.Language, request.Notify ?? true, TrustedAppOrigin(http, configuration, environment));
                return (await service.InviteAsync(user.ToUserId(), id, applicationId, input, ct)).ToHttpResult(Results.Ok);
            })
            .WithName("InviteToInterview")
            .WithSummary("Invite a candidate to the AI interview")
            .WithDescription("Creates the interview (questions from what the evaluation could not confirm, then the mandatory requirements; `language` es, es-en or en), moves the application to INTERVIEW, cancels any earlier open interview and e-mails the link when `notify` is true. Needs a completed evaluation. `expiresAt` defaults to 3 days.")
            .Produces<InterviewInvitationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        vacancies.MapGet("{id:guid}/applications/{applicationId:guid}/resume/file", async (Guid id, Guid applicationId, ClaimsPrincipal user, ApplicationActionsService service, CancellationToken ct) =>
                (await service.GetResumeFileAsync(user.ToUserId(), id, applicationId, ct)).ToHttpResult(file => Results.File(file.Content, file.ContentType)))
            .WithName("GetApplicationResumeFile")
            .WithSummary("The original CV file of an application")
            .WithDescription("Streams the file the candidate sent. Company-scoped; every download is audited (`cv.downloaded`).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapPost("{id:guid}/applications/{applicationId:guid}/ai-error", async (Guid id, Guid applicationId, AiErrorRequest request, ClaimsPrincipal user, ApplicationActionsService service, CancellationToken ct) =>
                (await service.FlagAiErrorAsync(user.ToUserId(), id, applicationId, new AiErrorInput(request.Kind, request.RequirementId, request.Note), ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("FlagAiError")
            .WithSummary("Flag an error of the AI in an evaluation")
            .WithDescription("`kind` is equivalence, missed, years or citation. It is recorded in the audit log and counted in governance as an AI error flagged by a person.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        vacancies.MapPost("{id:guid}/applications/{applicationId:guid}/request-original", async (Guid id, Guid applicationId, ClaimsPrincipal user, ApplicationActionsService service, CancellationToken ct) =>
                (await service.RequestOriginalAsync(user.ToUserId(), id, applicationId, ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("RequestOriginalCv")
            .WithSummary("Ask the candidate to send the CV again")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/api/interviews/{id:guid}/schedule", async (Guid id, ScheduleTeamInterviewRequest request, ClaimsPrincipal user, CandidateInterviewService service, CancellationToken ct) =>
                (await service.ScheduleWithTeamAsync(user.ToUserId(), id, new ScheduleTeamInterviewInput(request.Date, request.Time, request.DurationMin, request.Guests), ct)).ToHttpResult(_ => Results.NoContent()))
            .WithName("ScheduleTeamInterview")
            .WithSummary("Schedule the interview with the team")
            .WithDescription("E-mails the candidate and the guests with the date and leaves it in the audit log.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static string? Email(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true && user.FindFirstValue("email") is { Length: > 0 } email ? email.Trim().ToLowerInvariant() : null;

    /// <summary>Supabase puts user_metadata in the token as JSON; the portal only needs a fallback name.</summary>
    private static string? SessionName(this ClaimsPrincipal user)
    {
        var metadata = user.FindFirstValue("user_metadata");
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String ? name.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static IResult NoEmail() =>
        Results.Problem(detail: "The session has no e-mail.", statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Origin for links that carry a sign-in token. Never taken from a header in production: an attacker could
    /// set Origin to their own site and receive the token. Only PUBLIC_APP_URL, or localhost while developing.
    /// </summary>
    private static string TrustedAppOrigin(HttpRequest http, IConfiguration configuration, IHostEnvironment environment)
    {
        if (configuration["PUBLIC_APP_URL"] is { Length: > 0 } configured)
        {
            return configured;
        }

        if (environment.IsDevelopment()
            && Uri.TryCreate(http.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
            && origin.IsLoopback)
        {
            return origin.GetLeftPart(UriPartial.Authority);
        }

        return "http://localhost:3000";
    }
}
