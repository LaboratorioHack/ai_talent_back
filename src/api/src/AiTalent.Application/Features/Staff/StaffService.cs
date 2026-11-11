using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Staff;

public sealed class StaffService(
    IStaffRepository staff,
    StaffAccess access,
    IAuditLog audit,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<IReadOnlyList<StaffMember>>> ListAsync(Actor actor, CancellationToken ct = default)
    {
        var admin = await access.GetAdminAsync(actor, ct);
        if (!admin.IsSuccess)
        {
            return Result<IReadOnlyList<StaffMember>>.Failure(admin.Error!);
        }

        var members = await staff.ListByCompanyAsync(admin.Value!.CompanyId, ct);
        return Result<IReadOnlyList<StaffMember>>.Success(members);
    }

    public async Task<Result<StaffMember>> UpdateAsync(
        Actor actor,
        Guid targetUserId,
        StaffRole? role,
        bool? active,
        CancellationToken ct = default)
    {
        var admin = await access.GetAdminAsync(actor, ct);
        if (!admin.IsSuccess)
        {
            return admin;
        }

        if (targetUserId == actor.UserId)
        {
            return Result<StaffMember>.Failure(ErrorKind.Forbidden, "Admins cannot change their own role or status.");
        }

        var target = await staff.FindByUserIdAsync(targetUserId, ct);
        if (target is null || target.CompanyId != admin.Value!.CompanyId)
        {
            return Result<StaffMember>.Failure(ErrorKind.NotFound, "Staff member not found.");
        }

        // Admins can't remove other admins. Two admins demoting each other at the same time would leave
        // the company without one, so a company's admins can only be changed directly in the database.
        if (target.Role == StaffRole.Admin && (role is { } requested && requested != StaffRole.Admin || active is false))
        {
            return Result<StaffMember>.Failure(ErrorKind.Forbidden, "Admins can't demote or deactivate other admins.");
        }

        if (role is { } newRole)
        {
            target.ChangeRole(newRole);
        }

        if (active is { } isActive)
        {
            target.SetActive(isActive);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actor.UserId,
            "staff.updated",
            "staff",
            targetUserId.ToString(),
            new { role = role?.ToString().ToUpperInvariant(), active },
            ct);

        return Result<StaffMember>.Success(target);
    }
}
