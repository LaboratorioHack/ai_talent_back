using System.Net.Http.Json;
using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;

namespace AiTalent.Infrastructure.AiEngine;

internal sealed class AiEngineClient(HttpClient httpClient) : IAiEngineClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

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

    public async Task<CvIngestionResponse> IngestCvAsync(
        IngestCvRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.FromStorage)
        {
            return await PostJsonAsync<IngestCvRequest, CvIngestionResponse>(
                "v1/cvs/ingest-json",
                new IngestCvRequest(request.ResumeId),
                cancellationToken);
        }

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(request.ResumeId.ToString()), "resume_id");
        using var response = await httpClient.PostAsync(new Uri("v1/cvs/ingest", UriKind.Relative), form, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CvIngestionResponse>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("Empty response from AI Engine.");
    }

    public Task<CandidateEvaluationResponse> EvaluateAsync(
        EvaluateCandidateRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<EvaluateCandidateRequest, CandidateEvaluationResponse>(
            "v1/evaluations/evaluate",
            request,
            cancellationToken);

    public Task<RankCandidatesResponse> RankCandidatesAsync(
        RankCandidatesRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<RankCandidatesRequest, RankCandidatesResponse>(
            "v1/evaluations/rank",
            request,
            cancellationToken);

    public Task<EvidenceSearchResponse> SearchEvidenceAsync(
        EvidenceSearchRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<EvidenceSearchRequest, EvidenceSearchResponse>(
            "v1/rag/evidence",
            request,
            cancellationToken);

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string relativeUrl,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(relativeUrl, body, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("Empty response from AI Engine.");
    }
}
