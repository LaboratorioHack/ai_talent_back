using AiTalent.Application.Abstractions.AiEngine;

namespace AiTalent.Application.Abstractions;

/// <summary>Puerto hacia el AI Engine (Python): ingesta, extracción y evaluación de CVs.</summary>
public interface IAiEngineClient
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

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
}
