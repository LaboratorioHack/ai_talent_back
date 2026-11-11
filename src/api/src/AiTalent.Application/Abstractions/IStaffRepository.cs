using AiTalent.Domain.Entities;

namespace AiTalent.Application.Abstractions;

public interface IStaffRepository
{
    Task<StaffMember?> FindByUserIdAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<StaffMember>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default);

    void Add(StaffMember staff);
}
