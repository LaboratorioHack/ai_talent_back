using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AiTalent.Application.Abstractions;

namespace AiTalent.Infrastructure.Auth;

public sealed class SupabaseIdentityProvider(HttpClient http) : IIdentityProvider
{
    public async Task<SignUpOutcome> SignUpAsync(string email, string password, string? captchaToken, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["email"] = email, ["password"] = password };
        if (captchaToken is not null)
        {
            body["gotrue_meta_security"] = new { captcha_token = captchaToken };
        }

        try
        {
            using var response = await http.PostAsJsonAsync("signup", body, ct);
            if (response.IsSuccessStatusCode)
            {
                return SignUpOutcome.Created;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return SignUpOutcome.RateLimited;
            }

            if ((int)response.StatusCode >= 500)
            {
                return SignUpOutcome.Unavailable;
            }

            return ErrorCode(await response.Content.ReadAsStringAsync(ct)) switch
            {
                "user_already_exists" or "email_exists" => SignUpOutcome.AlreadyRegistered,
                "weak_password" => SignUpOutcome.WeakPassword,
                _ => SignUpOutcome.Rejected,
            };
        }
        catch (HttpRequestException)
        {
            return SignUpOutcome.Unavailable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SignUpOutcome.Unavailable;
        }
    }

    private static string? ErrorCode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error_code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
