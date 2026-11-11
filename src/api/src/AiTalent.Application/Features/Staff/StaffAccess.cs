using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Staff;

/// <summary>Reglas de acceso compartidas: pertenencia activa a una empresa, rol admin y MFA.</summary>
public sealed class StaffAccess(IStaffRepository staff)
{
    public async Task<Result<StaffMember>> GetActiveMemberAsync(Guid userId, CancellationToken ct = default)
    {
        var member = await staff.FindByUserIdAsync(userId, ct);
        if (member is null)
        {
            return Result<StaffMember>.Failure(ErrorKind.Forbidden, "You do not belong to a company yet.");
        }

        if (!member.Active)
        {
            return Result<StaffMember>.Failure(ErrorKind.Forbidden, "Your account is deactivated.");
        }

        return Result<StaffMember>.Success(member);
    }

    public async Task<Result<StaffMember>> GetAdminAsync(Actor actor, CancellationToken ct = default)
    {
        var member = await GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return member;
        }

        if (member.Value!.Role != StaffRole.Admin)
        {
            return Result<StaffMember>.Failure(ErrorKind.Forbidden, "Only admins can do this.");
        }

        if (!actor.MfaVerified)
        {
            return Result<StaffMember>.Failure(ErrorKind.Forbidden, "Admins must sign in with MFA.");
        }

        return member;
    }
}
