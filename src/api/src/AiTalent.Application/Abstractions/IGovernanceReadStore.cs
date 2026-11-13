namespace AiTalent.Application.Abstractions;

public sealed record AuditLogEntry(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string ActorType,
    string Action,
    string Entity,
    string? EntityId,
    string DetailsJson,
    string? ActorName,
    // What the entry is about, by name: the vacancy title or the candidate (null when there is none).
    string? SubjectName = null);

public interface IGovernanceReadStore
{
    Task<IReadOnlyList<AuditLogEntry>> ListForCompanyAsync(Guid companyId, int limit, DateTimeOffset since, CancellationToken ct = default);

    Task<int> CountHumanDecisionsAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default);

    Task<int> CountAiErrorsFlaggedAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default);
}
