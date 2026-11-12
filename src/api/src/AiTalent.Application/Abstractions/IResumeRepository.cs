using AiTalent.Domain.Entities;

namespace AiTalent.Application.Abstractions;

public interface IResumeRepository
{
    Task<int> GetNextVersionAsync(Guid candidateId, CancellationToken ct = default);

    Task<bool> ExistsWithHashAsync(Guid candidateId, string fileHash, CancellationToken ct = default);

    Task<Resume?> FindByIdAsync(Guid id, CancellationToken ct = default);

    void Add(Resume resume);

    void Remove(Resume resume);
}
