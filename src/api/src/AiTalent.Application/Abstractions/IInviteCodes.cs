using AiTalent.Domain.Enums;

namespace AiTalent.Application.Abstractions;

public sealed record InviteCode(string Code, DateTimeOffset ExpiresAt);

public sealed record InviteClaims(Guid CompanyId, StaffRole Role);

public interface IInviteCodes
{
    InviteCode Issue(Guid companyId, StaffRole role);

    /// <summary>Devuelve null si el código es inválido, está alterado o expiró.</summary>
    Task<InviteClaims?> ReadAsync(string code, CancellationToken ct = default);
}
