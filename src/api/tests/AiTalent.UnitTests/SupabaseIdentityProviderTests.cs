using System.Net;
using System.Text;
using AiTalent.Application.Abstractions;
using AiTalent.Infrastructure.Auth;

namespace AiTalent.UnitTests;

public class SupabaseIdentityProviderTests
{
    private static async Task<SignUpOutcome> Call(HttpStatusCode status, string body = "{}")
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
        return await Provider(handler).SignUpAsync("ada@example.com", "Correct-Horse-Battery-9!", null);
    }

    private static SupabaseIdentityProvider Provider(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://supabase.test/auth/v1/") });

    [Fact]
    public async Task SuccessfulSignUpIsCreated() =>
        Assert.Equal(SignUpOutcome.Created, await Call(HttpStatusCode.OK));

    [Fact]
    public async Task DuplicateEmailIsAlreadyRegistered() =>
        Assert.Equal(SignUpOutcome.AlreadyRegistered,
            await Call(HttpStatusCode.UnprocessableEntity, "{\"error_code\":\"user_already_exists\"}"));

    [Fact]
    public async Task WeakPasswordIsReported() =>
        Assert.Equal(SignUpOutcome.WeakPassword,
            await Call(HttpStatusCode.UnprocessableEntity, "{\"error_code\":\"weak_password\"}"));

    [Fact]
    public async Task OtherClientErrorsAreRejected() =>
        Assert.Equal(SignUpOutcome.Rejected, await Call(HttpStatusCode.BadRequest, "not json"));

    [Fact]
    public async Task SupabaseRateLimitIsReportedAsRateLimited() =>
        Assert.Equal(SignUpOutcome.RateLimited,
            await Call((HttpStatusCode)429, "{\"error_code\":\"over_email_send_rate_limit\"}"));

    [Fact]
    public async Task ServerErrorIsUnavailable() =>
        Assert.Equal(SignUpOutcome.Unavailable, await Call(HttpStatusCode.InternalServerError));

    [Fact]
    public async Task NetworkFailureIsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("supabase is down"));

        var outcome = await Provider(handler).SignUpAsync("ada@example.com", "Correct-Horse-Battery-9!", null);

        Assert.Equal(SignUpOutcome.Unavailable, outcome);
    }
}
