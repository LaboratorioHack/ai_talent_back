using AiTalent.Application.Abstractions;

namespace AiTalent.Api.Endpoints;

internal static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
            .WithName("Health")
            .AllowAnonymous();

        app.MapGet("/api/health/engine", async (IAiEngineClient engine, CancellationToken ct) =>
                await engine.IsHealthyAsync(ct) ? Results.Ok(new { engine = "ok" }) : Results.StatusCode(503))
            .WithName("EngineHealth")
            .AllowAnonymous();

        return app;
    }
}
