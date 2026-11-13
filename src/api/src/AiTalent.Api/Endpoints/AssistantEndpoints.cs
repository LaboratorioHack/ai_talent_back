using System.Security.Claims;
using AiTalent.Application.Features.Assistant;

namespace AiTalent.Api.Endpoints;

/// <summary>The Criba AI chat. One body for both portals; each endpoint decides what context the AI gets.</summary>
public sealed record AssistantChatApiRequest(IReadOnlyList<AssistantMessage>? Messages, Guid? VacancyId);

internal static class AssistantEndpoints
{
    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/chat", async (AssistantChatApiRequest request, ClaimsPrincipal user, AssistantService service, CancellationToken ct) =>
                (await service.AskAsCompanyAsync(user.ToActor(), request.Messages, request.VacancyId, ct)).ToHttpResult(Results.Ok))
            .WithName("AskAssistantAsCompany")
            .WithSummary("Chat with Criba AI (organization)")
            .WithDescription("Criba AI answers the last user message with the organization's dashboard and, when `vacancyId` is sent, that vacancy's profile and ranking. 1–20 messages, the last one from the user. The question is audited, not its text. Any active staff member.")
            .Produces<AssistantReply>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
