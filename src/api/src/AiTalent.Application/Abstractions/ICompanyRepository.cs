using AiTalent.Domain.Entities;

namespace AiTalent.Application.Abstractions;

public interface ICompanyRepository
{
    Task<Company?> FindByIdAsync(Guid id, CancellationToken ct = default);

    void Add(Company company);
}
