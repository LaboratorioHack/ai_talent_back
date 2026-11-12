namespace AiTalent.Api.Endpoints;

internal sealed record CreateCompanyRequest(string? CompanyName, string? Sector, string? Size, string? YourName);

internal sealed record JoinCompanyRequest(string? Code, string? YourName);

internal sealed record UpdateStaffRequest(string? Role, bool? Active);

internal sealed record CompanyResponse(Guid Id, string Name, string? Sector, string? Size, string Role);

internal sealed record InviteCodeResponse(string Code, DateTimeOffset ExpiresAt);

internal sealed record StaffResponse(Guid UserId, string Name, string Role, bool Active);

internal sealed record MeResponse(Guid UserId, string? Email, string? Name, bool MfaVerified, CompanyResponse? Company);
