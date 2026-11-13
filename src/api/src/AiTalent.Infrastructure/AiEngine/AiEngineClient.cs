using System.Net.Http.Json;
using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using Microsoft.Extensions.Logging;

namespace AiTalent.Infrastructure.AiEngine;

internal sealed class AiEngineClient(HttpClient httpClient, ILogger<AiEngineClient> logger) : IAiEngineClient
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

    public async Task<EngineReadiness> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(new Uri("health/ready", UriKind.Relative), cancellationToken);
            var checks = response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken)
                : (JsonElement?)null;
            return new EngineReadiness((int)response.StatusCode, checks);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "AI Engine readiness call failed");
            return new EngineReadiness(0, null);
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
        await EnsureSuccessAsync(response, "v1/cvs/ingest", cancellationToken);
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

    public Task<CompareCandidatesResponse> CompareAsync(
        CompareCandidatesRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<CompareCandidatesRequest, CompareCandidatesResponse>(
            "v1/evaluations/compare",
            request,
            cancellationToken);

    public Task<ProposeRequirementsResponse> ProposeRequirementsAsync(
        ProposeRequirementsRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<ProposeRequirementsRequest, ProposeRequirementsResponse>(
            "v1/role-profiles/propose",
            request,
            cancellationToken);

    public Task<DraftVacancyResponse> DraftVacancyAsync(
        DraftVacancyRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<DraftVacancyRequest, DraftVacancyResponse>(
            "v1/role-profiles/draft",
            request,
            cancellationToken);

    public Task<SuggestEquivalencesResponse> SuggestEquivalencesAsync(
        SuggestEquivalencesRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<SuggestEquivalencesRequest, SuggestEquivalencesResponse>(
            "v1/role-profiles/equivalences",
            request,
            cancellationToken);

    public Task<AssistantChatResponse> ChatAsync(
        AssistantChatRequest request,
        CancellationToken cancellationToken = default) =>
        PostJsonAsync<AssistantChatRequest, AssistantChatResponse>(
            "v1/assistant/chat",
            request,
            cancellationToken);

    private async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string relativeUrl,
        TRequest body,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(relativeUrl, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, relativeUrl, cancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException("Empty response from AI Engine.");
    }

    /// <summary>Callers map any failure to "engine unavailable"; the real status and reason stay in the log.</summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage response, string route, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "AI Engine {Route} answered {Status}: {Detail}",
            route,
            (int)response.StatusCode,
            detail.Length > 500 ? detail[..500] : detail);
        response.EnsureSuccessStatusCode();
    }
}
