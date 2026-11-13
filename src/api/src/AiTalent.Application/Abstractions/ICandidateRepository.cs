using AiTalent.Domain.Entities;

namespace AiTalent.Application.Abstractions;

public interface ICandidateRepository
{
    Task<Candidate?> FindByCompanyAndEmailAsync(Guid companyId, string normalizedEmail, CancellationToken ct = default);

    Task<Candidate?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Every company's candidate row for one person (the e-mail is stored lowercase).</summary>
    Task<IReadOnlyList<Candidate>> ListByEmailAsync(string normalizedEmail, CancellationToken ct = default);

    void Add(Candidate candidate);

    void Remove(Candidate candidate);
}
