using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;

namespace AiTalent.Application.Features.Auth;

public sealed record SignUpAccepted;

public sealed class SignUpService(IIdentityProvider identity)
{
    public async Task<Result<SignUpAccepted>> SignUpAsync(string? email, string? password, string? captchaToken, CancellationToken ct = default)
    {
        if (!Validation.IsEmail(email))
        {
            return Result<SignUpAccepted>.Failure(ErrorKind.Invalid, "Enter a valid email address.");
        }

        if (!Validation.IsPassword(password))
        {
            return Result<SignUpAccepted>.Failure(ErrorKind.Invalid, "Password must be between 12 and 72 characters.");
        }

        var outcome = await identity.SignUpAsync(email.Trim(), password, captchaToken, ct);
        return outcome switch
        {
            SignUpOutcome.Unavailable => Result<SignUpAccepted>.Failure(ErrorKind.Unavailable, "Sign-up is temporarily unavailable. Try again later."),
            SignUpOutcome.WeakPassword => Result<SignUpAccepted>.Failure(ErrorKind.Invalid, "Password is too weak. Mix upper and lower case letters, digits and symbols."),
            SignUpOutcome.Rejected => Result<SignUpAccepted>.Failure(ErrorKind.Invalid, "Sign-up could not be completed."),
            SignUpOutcome.RateLimited => Result<SignUpAccepted>.Failure(ErrorKind.TooManyRequests, "Too many sign-up attempts. Wait a few minutes and try again."),
            // Created and AlreadyRegistered get the same answer, so the endpoint can't reveal which emails have accounts.
            _ => Result<SignUpAccepted>.Success(new SignUpAccepted()),
        };
    }
}
