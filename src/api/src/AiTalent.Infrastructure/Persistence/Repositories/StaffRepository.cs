using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiTalent.Infrastructure.Persistence.Repositories;

internal sealed class StaffRepository(AppDbContext db) : IStaffRepository
{
    public Task<StaffMember?> FindByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        db.StaffMembers.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public async Task<IReadOnlyList<StaffMember>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await db.StaffMembers
            .Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public void Add(StaffMember staff) => db.StaffMembers.Add(staff);
}
