using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiTalent.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiTalent.Infrastructure.Auth;

/// <summary>
/// Supabase Auth admin API with the service role key. Used to issue applicant magic links that Criba sends
/// through its own SMTP, so they are not subject to Supabase's shared e-mail rate limit.
/// </summary>
internal sealed class SupabaseAuthAdmin(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<SupabaseAuthAdmin> logger) : IAuthAdmin
{
    public async Task<string?> CreateMagicLinkTokenAsync(string email, CancellationToken ct = default)
    {
        var url = configuration["SUPABASE_URL"]?.TrimEnd('/');
        var key = configuration["SUPABASE_SERVICE_ROLE_KEY"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required to send applicant links.");
        }

        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        client.DefaultRequestHeaders.Add("apikey", key);

        var token = await GenerateAsync(client, url, email, ct);
        if (token is not null)
        {
            return token;
        }

        // A first-time applicant has no Auth user yet: create it already confirmed (they own the inbox the link goes to).
        using var created = await client.PostAsJsonAsync($"{url}/auth/v1/admin/users", new { email, email_confirm = true }, ct);
        if (!created.IsSuccessStatusCode && created.StatusCode != HttpStatusCode.UnprocessableEntity)
        {
            logger.LogWarning("Supabase refused to create an applicant user ({Status}).", (int)created.StatusCode);
            return null;
        }

        return await GenerateAsync(client, url, email, ct);
    }

    private async Task<string?> GenerateAsync(HttpClient client, string url, string email, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync($"{url}/auth/v1/admin/generate_link", new { type = "magiclink", email }, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation("Supabase did not generate a magic link ({Status}).", (int)response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<GenerateLinkResponse>(cancellationToken: ct);
        return body?.HashedToken ?? body?.Properties?.HashedToken;
    }

    private sealed record GenerateLinkResponse(
        [property: JsonPropertyName("hashed_token")] string? HashedToken,
        [property: JsonPropertyName("properties")] GenerateLinkProperties? Properties);

    private sealed record GenerateLinkProperties([property: JsonPropertyName("hashed_token")] string? HashedToken);
}
