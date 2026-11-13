using System.Security.Claims;
using AiTalent.Application.Features.Governance;

namespace AiTalent.Api.Endpoints;

internal static class GovernanceEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var governance = app.MapGroup("/api/governance");

        governance.MapGet("audit", async (int? days, ClaimsPrincipal user, GovernanceService service, CancellationToken ct) =>
                (await service.GetAuditAsync(user.ToUserId(), days ?? 30, ct)).ToHttpResult(Results.Ok))
            .WithName("GetGovernanceAudit")
            .WithSummary("Governance audit")
            .WithDescription("Organization audit stats, attributes used by the AI and a human-readable decision log for the last `days` (7, 30 or 90; default 30).")
            .Produces<GovernanceAuditResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
