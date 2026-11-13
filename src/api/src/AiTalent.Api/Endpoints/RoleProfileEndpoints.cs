using System.Security.Claims;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Features.RoleProfiles;

namespace AiTalent.Api.Endpoints;

/// <summary>AI help for the role-profile editor. Nothing is saved: the recruiter reviews and saves.</summary>
internal static class RoleProfileEndpoints
{
    public static IEndpointRouteBuilder MapRoleProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profiles = app.MapGroup("/api/role-profiles");

        profiles.MapPost("propose", async (
                ProposeRequirementsApiRequest request,
                ClaimsPrincipal user,
                RoleProfileAssistService service,
                CancellationToken ct) =>
            {
                var existing = request.Existing?
                    .Select(item => new ExistingRequirementInput(item.Description, item.Type, item.Weight))
                    .ToList();
                var result = await service.ProposeAsync(user.ToActor(), request.Description, request.Title, request.Area, existing, ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("ProposeRequirements")
            .WithSummary("Propose requirements from a job description")
            .WithDescription("The AI Engine (`/v1/role-profiles/propose`) reads the job description (40–4000 characters) and proposes 3 to 8 requirements with `type` (`MANDATORY`, `DESIRABLE`, `DISQUALIFYING`), `weight` and `equivalences` (`label`, `percent`). Send the requirements already in the editor in `existing`: they are not repeated, and the new MANDATORY/DESIRABLE weights fill what is left to reach 100. Never uses protected attributes. Any active staff member.")
            .Produces<ProposeRequirementsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        profiles.MapPost("equivalences", async (
                SuggestEquivalencesApiRequest request,
                ClaimsPrincipal user,
                RoleProfileAssistService service,
                CancellationToken ct) =>
            {
                var result = await service.SuggestEquivalencesAsync(user.ToActor(), request.Requirement, request.Title, request.Description, request.Existing, ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("SuggestEquivalences")
            .WithSummary("Suggest equivalences for one requirement")
            .WithDescription("The AI Engine (`/v1/role-profiles/equivalences`) suggests up to 4 alternative skills that partially cover `requirement`, each with the `percent` it covers. `title` and `description` give context; labels in `existing` are not repeated. Any active staff member.")
            .Produces<SuggestEquivalencesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
