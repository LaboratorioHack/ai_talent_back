using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Abstractions;

public interface IJobOpeningRepository
{
    Task<IReadOnlyList<JobOpening>> ListByCompanyAsync(
        Guid companyId,
        JobOpeningStatus? status,
        string? search,
        int skip,
        int take,
        CancellationToken ct = default);

    Task<int> CountByCompanyAsync(
        Guid companyId,
        JobOpeningStatus? status,
        string? search,
        CancellationToken ct = default);

    Task<JobOpening?> FindByIdAsync(Guid companyId, Guid id, CancellationToken ct = default);

    /// <summary>Loads a non-deleted vacancy for public application checks (no company scoping).</summary>
    Task<JobOpening?> FindForApplicationAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads a non-deleted vacancy with its profiles and requirements for the public page.</summary>
    Task<JobOpening?> FindPublicAsync(Guid id, CancellationToken ct = default);

    Task<bool> IsProfileInUseAsync(Guid roleProfileId, CancellationToken ct = default);

    void Add(JobOpening jobOpening);
}
