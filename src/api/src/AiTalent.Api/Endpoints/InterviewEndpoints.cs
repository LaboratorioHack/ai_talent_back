using System.Security.Claims;
using AiTalent.Application.Features.Interviews;

namespace AiTalent.Api.Endpoints;

internal static class InterviewEndpoints
{
    public static IEndpointRouteBuilder MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        var interviews = app.MapGroup("/api/interviews");

        interviews.MapGet("", async (ClaimsPrincipal user, InterviewService service, CancellationToken ct) =>
                (await service.ListAsync(user.ToUserId(), ct)).ToHttpResult(Results.Ok))
            .WithName("ListInterviews")
            .WithSummary("List interviews")
            .WithDescription("Interviews for the caller's organization, newest first.")
            .Produces<IReadOnlyList<InterviewResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        interviews.MapGet("{id:guid}", async (
                Guid id,
                ClaimsPrincipal user,
                InterviewService service,
                CancellationToken ct) =>
            (await service.GetAsync(user.ToUserId(), id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetInterview")
            .WithSummary("Get an interview")
            .WithDescription("Interview detail with turns, contrasts and recommendation. Other companies return 404.")
            .Produces<InterviewResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
