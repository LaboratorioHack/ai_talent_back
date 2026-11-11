using System.Security.Claims;
using AiTalent.Application.Features.Companies;

namespace AiTalent.Api.Endpoints;

internal static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var companies = app.MapGroup("/api/companies");

        companies.MapPost("", async (CreateCompanyRequest request, ClaimsPrincipal user, CompanyService service, CancellationToken ct) =>
                (await service.CreateAsync(user.ToUserId(), request.CompanyName, request.YourName, ct))
                .ToHttpResult(membership => Results.Created($"/api/companies/{membership.CompanyId}", membership.ToResponse())))
            .WithName("CreateCompany")
            .WithSummary("Create a company")
            .WithDescription("Creates a company and makes the signed-in user its admin. Only works for users who don't belong to a company yet. `companyName` and `yourName` are required, up to 120 characters each.")
            .Produces<CompanyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        companies.MapPost("current/invite-codes", async (ClaimsPrincipal user, CompanyService service, CancellationToken ct) =>
                (await service.CreateInviteCodeAsync(user.ToActor(), ct))
                .ToHttpResult(invite => Results.Ok(new InviteCodeResponse(invite.Code, invite.ExpiresAt))))
            .WithName("CreateInviteCode")
            .WithSummary("Create an invite code")
            .WithDescription("Creates a code that lets a colleague join this company as a recruiter. The code expires after 7 days and can be used by anyone who has it until then. Admins only, and the session must have completed MFA.")
            .Produces<InviteCodeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        companies.MapPost("join", async (JoinCompanyRequest request, ClaimsPrincipal user, CompanyService service, CancellationToken ct) =>
                (await service.JoinAsync(user.ToUserId(), request.Code, request.YourName, ct))
                .ToHttpResult(membership => Results.Ok(membership.ToResponse())))
            .RequireRateLimiting(RateLimitPolicies.InviteJoin)
            .WithName("JoinCompany")
            .WithSummary("Join a company with an invite code")
            .WithDescription("Adds the signed-in user to the company that issued the code, as a recruiter. Only works for users who don't belong to a company yet. Expired, altered, or unknown codes all return the same 400 response. Limited to 10 attempts per user every 15 minutes.")
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
