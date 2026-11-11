using AiTalent.Application.Abstractions;

namespace AiTalent.Infrastructure.AiEngine;

internal sealed class AiEngineClient(HttpClient httpClient) : IAiEngineClient
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(new Uri("health", UriKind.Relative), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
