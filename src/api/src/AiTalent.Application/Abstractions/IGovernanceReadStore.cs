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
    string? ActorName);

public interface IGovernanceReadStore
{
    Task<IReadOnlyList<AuditLogEntry>> ListForCompanyAsync(Guid companyId, int limit, CancellationToken ct = default);

    Task<int> CountHumanDecisionsAsync(Guid companyId, CancellationToken ct = default);

    Task<int> CountAiErrorsFlaggedAsync(Guid companyId, CancellationToken ct = default);
}
