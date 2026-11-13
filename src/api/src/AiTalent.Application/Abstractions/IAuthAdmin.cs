namespace AiTalent.Application.Abstractions;

/// <summary>Supabase Auth admin operations (service role, server only).</summary>
public interface IAuthAdmin
{
    /// <summary>
    /// Creates a one-time magic-link token for the e-mail (creating the user if needed) without Supabase sending
    /// any e-mail. Returns the hashed token the web app verifies with <c>verifyOtp</c>, or null if Auth refused it.
    /// </summary>
    Task<string?> CreateMagicLinkTokenAsync(string email, CancellationToken ct = default);
}
