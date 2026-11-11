using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class CompanyRepository(AppDbContext db) : ICompanyRepository
{
    public Task<Company?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);

    public void Add(Company company) => db.Companies.Add(company);
}
