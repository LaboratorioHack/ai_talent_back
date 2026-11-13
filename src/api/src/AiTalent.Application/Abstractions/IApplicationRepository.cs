using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.Application.Abstractions;

public interface IApplicationRepository
{
    Task<ApplicationEntity?> FindByJobAndCandidateAsync(Guid jobOpeningId, Guid candidateId, CancellationToken ct = default);

    Task<ApplicationEntity?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ApplicationSubmission>> ListByJobOpeningAsync(Guid jobOpeningId, CancellationToken ct = default);

    /// <summary>
    /// True when the person applied to a vacancy themselves: an application with source CANDIDATE sent from
    /// their session or under their e-mail. CVs a recruiter uploaded on their behalf do not count.
    /// </summary>
    Task<bool> HasSelfSubmittedAsync(Guid userId, string? normalizedEmail, CancellationToken ct = default);

    void Add(ApplicationEntity application);

    void Remove(ApplicationEntity application);
}
