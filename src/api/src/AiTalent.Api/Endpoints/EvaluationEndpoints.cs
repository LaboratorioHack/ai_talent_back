using System.Security.Claims;
using AiTalent.Application.Features.Evaluations;

namespace AiTalent.Api.Endpoints;

/// <summary>Staff endpoints over the AI Engine: evaluate, rank, compare and evidence (RAG).</summary>
internal static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        var vacancies = app.MapGroup("/api/vacancies");

        vacancies.MapPost("{id:guid}/applications/{applicationId:guid}/evaluate", async (
                Guid id,
                Guid applicationId,
                ClaimsPrincipal user,
                EvaluationService service,
                CancellationToken ct) =>
            {
                var result = await service.EvaluateAsync(user.ToActor(), id, applicationId, ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("EvaluateApplication")
            .WithSummary("Evaluate one application with the AI")
            .WithDescription("Evaluates the application's latest processed CV against the vacancy's current role profile (AI Engine `/v1/evaluations/evaluate`). The engine stores the evaluation, so the vacancy counts update. Requires a processed CV (409 otherwise) and a profile with requirements (400).")
            .Produces<ApplicationEvaluation>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        vacancies.MapPost("{id:guid}/rank", async (
                Guid id,
                RankApplicationsRequest? request,
                ClaimsPrincipal user,
                EvaluationService service,
                CancellationToken ct) =>
            {
                var result = await service.RankAsync(user.ToActor(), id, request?.ApplicationIds, ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("RankApplications")
            .WithSummary("Evaluate and rank a vacancy's applications")
            .WithDescription("Evaluates and ranks applications against the current role profile (AI Engine `/v1/evaluations/rank`), best first. Without a body (or with an empty `applicationIds`) it ranks every application with a processed CV that is not withdrawn or rejected, up to 100. Applications the engine could not evaluate come back in `notRanked`. Can take a while: one AI evaluation per CV.")
            .Produces<VacancyRanking>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        vacancies.MapPost("{id:guid}/compare", async (
                Guid id,
                CompareApplicationsRequest request,
                ClaimsPrincipal user,
                EvaluationService service,
                CancellationToken ct) =>
            {
                var result = await service.CompareAsync(user.ToActor(), id, request.ApplicationIds, ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("CompareApplications")
            .WithSummary("Compare 2 or 3 applications with the AI")
            .WithDescription("Side-by-side trade-offs of 2 or 3 applications against the current role profile (AI Engine `/v1/evaluations/compare`). `candidates` maps each `resumeId` (the engine's `candidateId` in the matrix) to its application and name.")
            .Produces<ApplicationComparison>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        vacancies.MapPost("{id:guid}/applications/{applicationId:guid}/evidence", async (
                Guid id,
                Guid applicationId,
                EvidenceSearchApiRequest request,
                ClaimsPrincipal user,
                EvaluationService service,
                CancellationToken ct) =>
            {
                var result = await service.SearchEvidenceAsync(user.ToActor(), id, applicationId, request.Query, request.Limit, ct);
                return result.ToHttpResult(Results.Ok);
            })
            .WithName("SearchApplicationEvidence")
            .WithSummary("Search evidence in an application's CV")
            .WithDescription("Semantic search over the anonymized fragments of the application's latest CV (AI Engine `/v1/rag/evidence`). `query` 2–500 characters, `limit` 1–20 (default 8). Each chunk carries the citation and page to show as evidence.")
            .Produces<ApplicationEvidence>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
