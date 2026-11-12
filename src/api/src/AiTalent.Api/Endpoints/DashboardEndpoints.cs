using System.Security.Claims;
using AiTalent.Application.Features.Dashboard;

namespace AiTalent.Api.Endpoints;

internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (
                ClaimsPrincipal user,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetDashboardAsync(user.ToActor(), ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("GetDashboard")
            .WithSummary("Home dashboard")
            .WithDescription("Open vacancies with stats for the last 7, 30 and 90 days (`byPeriod`, by application date), the top 5 evaluated profiles and pending items (unreadable or duplicate CVs). `closesAt` is `YYYY-MM-DD` in Bogotá time, empty when the vacancy has no closing date.")
            .Produces<DashboardData>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/notifications", async (
                ClaimsPrincipal user,
                DashboardService service,
                CancellationToken ct) =>
            {
                var result = await service.GetNoticesAsync(user.ToActor(), ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("ListNotifications")
            .WithSummary("Bell notices")
            .WithDescription("Up to 10 notices, newest first, computed on the fly per vacancy: unreadable and duplicate CVs (warning), CVs evaluated and new applications in the last 7 days (success). `when` comes formatted in Spanish (Bogotá time); `occurredAt` is the raw instant.")
            .Produces<IReadOnlyList<NavNotice>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
