using System.Security.Claims;
using AiTalent.Application.Features.Auth;

namespace AiTalent.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/me", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
            {
                var result = await auth.GetMeAsync(user.ToActor(), ct);
                return result.ToHttpResult(me => Results.Ok(new MeResponse(
                    me.UserId,
                    user.FindFirstValue("email"),
                    me.Name,
                    me.MfaVerified,
                    me.Membership?.ToResponse())));
            })
            .WithName("GetMe")
            .WithSummary("Get the current user")
            .WithDescription("Returns the signed-in user's id, email, name, whether MFA was completed for this session, and their company and role. `name` comes from the company's staff record, so it is null until the user creates or joins a company. `company` is null until then too.")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // No public sign-up: registration is closed and accounts are provisioned server-side with the
        // Supabase Admin API (see scripts/provision-user.sh). Supabase Auth handles login and password reset.

        return app;
    }
}
