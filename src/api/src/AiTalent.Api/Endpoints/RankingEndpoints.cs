using System.Security.Claims;
using AiTalent.Application.Features.Rankings;

namespace AiTalent.Api.Endpoints;

internal static class RankingEndpoints
{
    public static IEndpointRouteBuilder MapRankingEndpoints(this IEndpointRouteBuilder app)
    {
        var rankings = app.MapGroup("/api/rankings");

        rankings.MapGet("", async (ClaimsPrincipal user, RankingService service, CancellationToken ct) =>
                (await service.ListSummariesAsync(user.ToUserId(), ct)).ToHttpResult(Results.Ok))
            .WithName("ListRankings")
            .WithSummary("List vacancy rankings")
            .WithDescription("Summaries for each open vacancy: evaluated counts and top three candidates.")
            .Produces<IReadOnlyList<RankingSummaryResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        rankings.MapGet("top", async (ClaimsPrincipal user, RankingService service, CancellationToken ct) =>
                (await service.ListTopPeopleAsync(user.ToUserId(), ct)).ToHttpResult(Results.Ok))
            .WithName("ListTopPeople")
            .WithSummary("Top candidates across vacancies")
            .WithDescription("Best evaluated candidates from all open vacancies, ordered by score.")
            .Produces<IReadOnlyList<TopPersonResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/vacancies/{id:guid}/ranking", async (
                Guid id,
                ClaimsPrincipal user,
                RankingService service,
                CancellationToken ct) =>
            (await service.GetByVacancyAsync(user.ToUserId(), id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetVacancyRanking")
            .WithSummary("Ranking for a vacancy")
            .WithDescription("Role profile, evaluated candidates and evidence. Uses the AI Engine `/v1/evaluations/rank` ordering when available.")
            .Produces<RankingDataResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
