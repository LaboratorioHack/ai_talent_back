namespace AiTalent.Application.Abstractions;

public enum SignUpOutcome
{
    Created,
    AlreadyRegistered,
    WeakPassword,
    Rejected,
    RateLimited,
    Unavailable,
}

/// <summary>Puerto hacia el proveedor de identidad (Supabase Auth).</summary>
public interface IIdentityProvider
{
    Task<SignUpOutcome> SignUpAsync(string email, string password, string? captchaToken, CancellationToken ct = default);
}
