namespace AiTalent.Application.Abstractions;

/// <summary>
/// One application with its candidate's latest resume and its latest completed evaluation.
/// Read-only projection for counts, the dashboard and notifications.
/// </summary>
public sealed record ApplicationFact(
    Guid ApplicationId,
    Guid JobOpeningId,
    string CandidateName,
    string SelectionStatus,
    DateTimeOffset AppliedAt,
    string? ResumeStatus,
    string? ResumeHash,
    DateTimeOffset? ResumeCreatedAt,
    string? Fulfillment,
    bool Disqualified,
    decimal? TotalScore,
    DateTimeOffset? EvaluatedAt);

public interface IVacancyInsights
{
    /// <summary>Applications of the given vacancies, scoped to the company.</summary>
    Task<IReadOnlyList<ApplicationFact>> ListApplicationFactsAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> vacancyIds,
        CancellationToken ct = default);
}
