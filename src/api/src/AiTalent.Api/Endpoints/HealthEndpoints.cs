using AiTalent.Application.Abstractions;

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

        return app;
    }
}
