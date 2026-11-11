using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Auth;

namespace AiTalent.UnitTests;

public class SignUpServiceTests
{
    private const string ValidPassword = "Correct-Horse-Battery-9!";

    private static Task<Result<SignUpAccepted>> SignUp(SignUpOutcome outcome, string? email = "ada@example.com", string? password = ValidPassword) =>
        new SignUpService(new FakeIdentityProvider(outcome)).SignUpAsync(email, password, null);

    [Fact]
    public async Task ValidSignUpIsAccepted()
    {
        var result = await SignUp(SignUpOutcome.Created);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AlreadyRegisteredEmailGetsTheSameAnswerAsNewAccount()
    {
        var result = await SignUp(SignUpOutcome.AlreadyRegistered);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData(null)]
    public async Task InvalidEmailIsRejected(string? email)
    {
        var result = await SignUp(SignUpOutcome.Created, email: email);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task ShortPasswordIsRejectedBeforeReachingSupabase()
    {
        var result = await SignUp(SignUpOutcome.Created, password: "short");

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task WeakPasswordFromSupabaseIsReported()
    {
        var result = await SignUp(SignUpOutcome.WeakPassword);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task SupabaseRateLimitIsReportedAsTooManyRequests()
    {
        var result = await SignUp(SignUpOutcome.RateLimited);

        Assert.Equal(ErrorKind.TooManyRequests, result.Error!.Kind);
    }

    [Fact]
    public async Task SupabaseOutageIsReportedAsUnavailable()
    {
        var result = await SignUp(SignUpOutcome.Unavailable);

        Assert.Equal(ErrorKind.Unavailable, result.Error!.Kind);
    }
}
