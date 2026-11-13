using System.Security.Claims;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health")
            .WithSummary("API health check")
            .WithDescription("Returns `{ \"status\": \"ok\" }` when the API is running. Doesn't require a login.")
            .AllowAnonymous();

        app.MapGet("/api/health/engine", async (IAiEngineClient engine, CancellationToken ct) =>
                await engine.IsHealthyAsync(ct) ? Results.Ok(new { engine = "ok" }) : Results.StatusCode(503))
            .WithName("EngineHealth")
            .WithSummary("AI Engine health check")
            .WithDescription("Returns 200 when the AI Engine responds and 503 when it doesn't. Doesn't require a login.")
            .AllowAnonymous();

        app.MapGet("/api/health/ai", async (ClaimsPrincipal user, StaffAccess access, IAiEngineClient engine, CancellationToken ct) =>
            {
                var member = await access.GetActiveMemberAsync(user.ToUserId(), ct);
                if (!member.IsSuccess)
                {
                    return Results.Forbid();
                }

                var readiness = await engine.GetReadinessAsync(ct);
                var auth = readiness.Status switch
                {
                    0 => "unreachable",
                    401 or 403 => "rejected",
                    _ => "ok",
                };
                return Results.Ok(new { engineStatus = readiness.Status, auth, checks = readiness.Checks });
            })
            .WithName("AiReadiness")
            .WithSummary("AI readiness (staff)")
            .WithDescription("Whether the API's key is accepted by the AI Engine and what the engine needs to work: provider keys present (yes/no), database and a tiny Claude call. Never returns secret values.");

        return app;
    }
}
