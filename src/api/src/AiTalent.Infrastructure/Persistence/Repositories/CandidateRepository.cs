using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class CandidateRepository(AppDbContext db) : ICandidateRepository
{
    public Task<Candidate?> FindByCompanyAndEmailAsync(Guid companyId, string normalizedEmail, CancellationToken ct = default) =>
        db.Candidates.FirstOrDefaultAsync(
            candidate => candidate.CompanyId == companyId && candidate.ContactEmail == normalizedEmail,
            ct);

    public Task<Candidate?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Candidates.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);

    public void Add(Candidate candidate) => db.Candidates.Add(candidate);

    public void Remove(Candidate candidate) => db.Candidates.Remove(candidate);
}
