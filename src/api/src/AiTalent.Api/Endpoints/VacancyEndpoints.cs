using System.Security.Claims;
using AiTalent.Application.Features.Candidates;
using AiTalent.Application.Features.Vacancies;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Api.Endpoints;

internal static class VacancyEndpoints
{
    private static readonly VacancyCountsResponse ZeroCounts = new(0, 0, 0, 0, 0, 0);

    public static IEndpointRouteBuilder MapVacancyEndpoints(this IEndpointRouteBuilder app)
    {
        var vacancies = app.MapGroup("/api/vacancies");

        vacancies.MapGet("", async (
                string? status,
                string? q,
                int? page,
                int? pageSize,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                JobOpeningStatus? parsedStatus = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (!ApiExtensions.TryParseVacancyStatus(status, out var value))
                    {
                        return Results.Problem(
                            detail: "Status must be DRAFT, OPEN or CLOSED.",
                            statusCode: StatusCodes.Status400BadRequest);
                    }

                    parsedStatus = value;
                }

                var result = await service.ListAsync(user.ToActor(), parsedStatus, q, page, pageSize, ct);
                return result.ToHttpResult(pageResult => Results.Ok(new VacancyPageResponse(
                    pageResult.Items.Select(ToSummary).ToList(),
                    pageResult.Page,
                    pageResult.PageSize,
                    pageResult.Total)));
            })
            .WithName("ListVacancies")
            .WithSummary("List vacancies")
            .WithDescription("Lists the caller's company vacancies, newest first. Filters: `status` (DRAFT, OPEN, CLOSED), `q` (title or area), and pagination with `page`/`pageSize` (default 1/20, max 100). Counts stay zero until CVs and evaluations exist.")
            .Produces<VacancyPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        vacancies.MapPost("", async (
                CreateVacancyRequest request,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                ProfileInput? profile = null;
                if (request.Description is not null || request.Requirements is not null)
                {
                    if (!TryBuildProfile(request.Description, request.Requirements, out var built, out var error))
                    {
                        return error!;
                    }

                    profile = built;
                }

                var result = await service.CreateAsync(
                    user.ToActor(),
                    request.Title,
                    request.Area,
                    request.ClosingAt,
                    request.Positions,
                    profile,
                    ct);

                return result.ToHttpResult(detail =>
                    Results.Created($"/api/vacancies/{detail.Vacancy.Id}", ToDetail(detail)));
            })
            .WithName("CreateVacancy")
            .WithSummary("Create a vacancy")
            .WithDescription("Creates a DRAFT vacancy with its optional role profile and requirements in one request. Requirements carry `type` (MANDATORY, DESIRABLE, DISQUALIFYING), `weight` (0–100) and `equivalences` as percentages, e.g. `{\"Azure\": 80}`. Weights only have to add up to 100 when publishing.")
            .Produces<VacancyDetailResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        vacancies.MapGet("{id:guid}", async (
                Guid id,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                var result = await service.GetAsync(user.ToUserId(), id, ct);
                return result.ToHttpResult(detail => Results.Ok(ToDetail(detail)));
            })
            .WithName("GetVacancy")
            .WithSummary("Get a vacancy")
            .WithDescription("Returns a vacancy with its current role profile, requirements and weight total. Vacancies from another company return 404.")
            .Produces<VacancyDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapPatch("{id:guid}", async (
                Guid id,
                UpdateVacancyRequest request,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                var result = await service.UpdateAsync(
                    user.ToActor(),
                    id,
                    request.Title,
                    request.Area,
                    request.ClosingAt,
                    request.Positions,
                    ct);
                return result.ToHttpResult(detail => Results.Ok(ToDetail(detail)));
            })
            .WithName("UpdateVacancy")
            .WithSummary("Update a vacancy")
            .WithDescription("Updates title, area, closing date and number of positions. The role profile is updated through `PUT /api/vacancies/{id}/profile`.")
            .Produces<VacancyDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapPost("{id:guid}/publish", async (
                Guid id,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                var result = await service.PublishAsync(user.ToUserId(), id, ct);
                return result.ToHttpResult(detail => Results.Ok(ToDetail(detail)));
            })
            .WithName("PublishVacancy")
            .WithSummary("Publish a vacancy")
            .WithDescription("Moves a draft to OPEN. Requires at least one requirement and a weight total of exactly 100. The closing date, if set, must be in the future.")
            .Produces<VacancyDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapPost("{id:guid}/close", async (
                Guid id,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                var result = await service.CloseAsync(user.ToUserId(), id, ct);
                return result.ToHttpResult(detail => Results.Ok(ToDetail(detail)));
            })
            .WithName("CloseVacancy")
            .WithSummary("Close a vacancy")
            .WithDescription("Moves an open vacancy to CLOSED. Closed vacancies keep their data so rankings and audit stay available.")
            .Produces<VacancyDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapDelete("{id:guid}", async (
                Guid id,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                var result = await service.DeleteAsync(user.ToUserId(), id, ct);
                return result.ToHttpResult(_ => Results.NoContent());
            })
            .WithName("DeleteVacancy")
            .WithSummary("Delete a vacancy")
            .WithDescription("Soft-deletes a vacancy: it disappears from lists and returns 404, but applications, evaluations and audit rows are kept.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapPut("{id:guid}/profile", async (
                Guid id,
                UpdateVacancyProfileRequest request,
                ClaimsPrincipal user,
                VacancyService service,
                CancellationToken ct) =>
            {
                if (!TryBuildProfile(request.Description, request.Requirements, out var profile, out var error))
                {
                    return error!;
                }

                var result = await service.UpdateProfileAsync(user.ToUserId(), id, profile, ct);
                return result.ToHttpResult(detail => Results.Ok(ToDetail(detail)));
            })
            .WithName("UpdateVacancyProfile")
            .WithSummary("Replace a vacancy's role profile")
            .WithDescription("Replaces the description and full set of requirements. If the current profile was already used by an evaluation it is immutable and a new version is created; otherwise it is updated in place. Send `requirements: []` to clear them.")
            .Produces<VacancyDetailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapGet("{id:guid}/applications", async (
                Guid id,
                ClaimsPrincipal user,
                ApplicationListService service,
                CancellationToken ct) =>
            {
                var result = await service.ListByVacancyAsync(user.ToUserId(), id, ct);
                return result.ToHttpResult(items => Results.Ok(items.Select(item => new VacancyApplicationResponse(
                    item.ApplicationId,
                    item.CandidateId,
                    item.CandidateName,
                    item.CandidateEmail,
                    item.Source,
                    item.SelectionStatus,
                    item.AppliedAt,
                    item.UpdatedAt,
                    item.ResumeId,
                    item.ResumeVersion,
                    item.ProcessingStatus,
                    item.ProcessingError,
                    item.ResumeCreatedAt)).ToList()));
            })
            .WithName("ListVacancyApplications")
            .WithSummary("List applications for a vacancy")
            .WithDescription("Lists candidate applications submitted to a vacancy, including candidate contact details and latest resume processing status. Company-scoped.")
            .Produces<IReadOnlyList<VacancyApplicationResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        vacancies.MapPost("{id:guid}/applications", async (
                Guid id,
                HttpRequest request,
                CandidateApplicationService service,
                CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.Problem(
                        detail: "A multipart form with the CV file is required.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                if (file is null)
                {
                    return Results.Problem(
                        detail: "A CV file is required (PDF or DOCX).",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var name = form["name"].ToString();
                var email = form["email"].ToString();
                var consent = TryParseConsent(form["consent"].ToString());

                var result = await service.ApplyAsync(
                    new ApplyToVacancyCommand(
                        id,
                        name,
                        email,
                        consent,
                        file.FileName,
                        file.ContentType,
                        file.Length,
                        file.OpenReadStream),
                    ct);

                return result.ToHttpResult(receipt => Results.Created(
                    $"/api/vacancies/{id}/applications/{receipt.ApplicationId}",
                    new CandidateApplicationResponse(
                        receipt.ApplicationId,
                        receipt.CandidateId,
                        receipt.ResumeId,
                        receipt.Status,
                        receipt.DuplicateContent,
                        "Your application was received.")));
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.CandidateApply)
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("ApplyToVacancy")
            .WithSummary("Submit a CV to a vacancy")
            .WithDescription("Public endpoint for a candidate to apply to an open vacancy with a single CV (PDF/DOCX, max 10 MB). Requires `name`, `email`, `consent=true`, and `file`. Reapplying to the same vacancy is rejected. Does not return scores or rankings.")
            .Produces<CandidateApplicationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static bool TryParseConsent(string? value) =>
        bool.TryParse(value, out var parsed) && parsed
        || string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static bool TryBuildProfile(
        string? description,
        IReadOnlyList<RequirementRequest>? requirements,
        out ProfileInput profile,
        out IResult? error)
    {
        var inputs = new List<RequirementInput>();
        if (requirements is not null)
        {
            for (var index = 0; index < requirements.Count; index++)
            {
                var requirement = requirements[index];
                if (requirement.Type is null
                    || !ApiExtensions.TryParseRequirementType(requirement.Type, out var type))
                {
                    profile = new ProfileInput(description, inputs);
                    error = Results.Problem(
                        detail: $"Requirement {index + 1}: type must be MANDATORY, DESIRABLE or DISQUALIFYING.",
                        statusCode: StatusCodes.Status400BadRequest);
                    return false;
                }

                inputs.Add(new RequirementInput(
                    type,
                    requirement.Category,
                    requirement.Description,
                    requirement.Condition,
                    requirement.Weight,
                    requirement.Equivalences));
            }
        }

        profile = new ProfileInput(description, inputs);
        error = null;
        return true;
    }

    private static VacancySummaryResponse ToSummary(JobOpening vacancy) =>
        new(
            vacancy.Id,
            vacancy.Title,
            vacancy.Area,
            StatusName(vacancy.Status),
            vacancy.ClosingAt,
            vacancy.Positions,
            vacancy.CurrentProfile?.Version,
            ZeroCounts,
            vacancy.CreatedAt,
            vacancy.UpdatedAt);

    private static VacancyDetailResponse ToDetail(VacancyDetail detail)
    {
        var vacancy = detail.Vacancy;
        var profile = vacancy.CurrentProfile;
        var requirements = profile is null
            ? new List<RequirementResponse>()
            : profile.Requirements.Select(ToResponse).ToList();

        return new VacancyDetailResponse(
            vacancy.Id,
            vacancy.Title,
            vacancy.Area,
            StatusName(vacancy.Status),
            vacancy.ClosingAt,
            vacancy.Positions,
            profile?.Id,
            profile?.Version,
            profile?.Description,
            profile?.WeightTotal ?? 0m,
            detail.ProfileInUse,
            requirements,
            ZeroCounts,
            vacancy.CreatedAt,
            vacancy.UpdatedAt);
    }

    private static RequirementResponse ToResponse(Requirement requirement) =>
        new(
            requirement.Id,
            requirement.Type.ToString().ToUpperInvariant(),
            requirement.Category,
            requirement.Description,
            requirement.Condition,
            requirement.Weight,
            requirement.Equivalences);

    private static string StatusName(JobOpeningStatus status) =>
        status.ToString().ToUpperInvariant();
}
