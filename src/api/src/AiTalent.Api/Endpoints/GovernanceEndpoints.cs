using System.Security.Claims;
using AiTalent.Application.Features.Governance;

namespace AiTalent.Api.Endpoints;

internal static class GovernanceEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var governance = app.MapGroup("/api/governance");

        governance.MapGet("audit", async (ClaimsPrincipal user, GovernanceService service, CancellationToken ct) =>
                (await service.GetAuditAsync(user.ToUserId(), ct)).ToHttpResult(Results.Ok))
            .WithName("GetGovernanceAudit")
            .WithSummary("Governance audit")
            .WithDescription("Organization audit stats, attributes used by the AI and a human-readable decision log.")
            .Produces<GovernanceAuditResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
