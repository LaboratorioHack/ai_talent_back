namespace AiTalent.Application.Abstractions;

public sealed record VacancyEvaluationSnapshot(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string ApplicationSource,
    Guid ResumeId,
    Guid EvaluationId,
    decimal TotalScore,
    string Fulfillment,
    bool Disqualified,
    decimal Confidence,
    DateTimeOffset EvaluatedAt,
    string? ProfessionalDataJson,
    IReadOnlyList<EvaluationRequirementSnapshot> Results);

public sealed record EvaluationRequirementSnapshot(
    Guid RequirementResultId,
    Guid RequirementId,
    string Result,
    decimal Score,
    decimal Confidence,
    string? Justification,
    IReadOnlyList<EvaluationEvidenceSnapshot> Evidence);

public sealed record EvaluationEvidenceSnapshot(
    Guid EvidenceId,
    string Citation,
    string? Location);

public sealed record VacancyEvaluationStats(
    Guid VacancyId,
    string Title,
    DateTimeOffset? ClosingAt,
    int ProfileVersion,
    int Received,
    int Meets,
    int Partial,
    int DoesNotMeet);

public interface IEvaluationReadStore
{
    Task<IReadOnlyList<VacancyEvaluationStats>> ListOpenVacancyStatsAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<VacancyEvaluationSnapshot>> ListLatestCompletedAsync(
        Guid vacancyId,
        Guid roleProfileId,
        CancellationToken ct = default);

    Task<GovernanceEvaluationStats> GetGovernanceEvaluationStatsAsync(Guid companyId, CancellationToken ct = default);
}

public sealed record GovernanceEvaluationStats(
    int ResumesEvaluated,
    int ScoresWithEvidencePercent,
    int SensitiveAttributesUsed);
