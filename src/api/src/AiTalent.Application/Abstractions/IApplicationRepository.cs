using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.Application.Abstractions;

public interface IApplicationRepository
{
    Task<ApplicationEntity?> FindByJobAndCandidateAsync(Guid jobOpeningId, Guid candidateId, CancellationToken ct = default);

    void Add(ApplicationEntity application);

    void Remove(ApplicationEntity application);
}
