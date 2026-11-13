using AiTalent.Application.Abstractions.AiEngine;

namespace AiTalent.Application.Abstractions;

/// <summary>Puerto hacia el AI Engine (Python): ingesta, extracción y evaluación de CVs.</summary>
/// <summary>HTTP status of the engine's readiness call and its JSON body, if any.</summary>
public sealed record EngineReadiness(int Status, System.Text.Json.JsonElement? Checks);

public interface IAiEngineClient
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>The engine's readiness flags (keys present, database, a tiny Claude call); never secret values.</summary>
    Task<EngineReadiness> GetReadinessAsync(CancellationToken cancellationToken = default);

    Task<CvIngestionResponse> IngestCvAsync(IngestCvRequest request, CancellationToken cancellationToken = default);

    Task<CandidateEvaluationResponse> EvaluateAsync(
        EvaluateCandidateRequest request,
        CancellationToken cancellationToken = default);

    Task<RankCandidatesResponse> RankCandidatesAsync(
        RankCandidatesRequest request,
        CancellationToken cancellationToken = default);

    Task<EvidenceSearchResponse> SearchEvidenceAsync(
        EvidenceSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<CompareCandidatesResponse> CompareAsync(
        CompareCandidatesRequest request,
        CancellationToken cancellationToken = default);

    Task<ProposeRequirementsResponse> ProposeRequirementsAsync(
        ProposeRequirementsRequest request,
        CancellationToken cancellationToken = default);

    Task<SuggestEquivalencesResponse> SuggestEquivalencesAsync(
        SuggestEquivalencesRequest request,
        CancellationToken cancellationToken = default);
}
