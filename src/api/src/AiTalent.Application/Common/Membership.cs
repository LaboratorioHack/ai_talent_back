using AiTalent.Domain.Enums;

namespace AiTalent.Application.Common;

public sealed record Membership(Guid CompanyId, string CompanyName, StaffRole Role);
