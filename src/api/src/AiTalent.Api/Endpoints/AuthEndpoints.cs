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
                    me.MfaVerified,
                    me.Membership?.ToResponse())));
            })
            .WithName("GetMe")
            .WithSummary("Get the current user")
            .WithDescription("Returns the signed-in user's id, whether MFA was completed for this session, and their company and role. `company` is null until the user creates or joins a company.")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPost("/api/auth/signup", async (SignUpRequest request, SignUpService service, CancellationToken ct) =>
                (await service.SignUpAsync(request.Email, request.Password, request.CaptchaToken, ct)).ToHttpResult(_ =>
                    Results.Accepted(value: new MessageResponse("Check your email to confirm your account."))))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.SignUp)
            .WithName("SignUp")
            .WithSummary("Sign up with email and password")
            .WithDescription("Creates an account and sends a confirmation email. The account can't log in until the link in that email is clicked. The response is the same for new and already-registered emails, so it can't be used to find out who has an account. `password` must be 12 to 72 characters. Limited to 10 attempts per IP address every 15 minutes.")
            .Produces<MessageResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
