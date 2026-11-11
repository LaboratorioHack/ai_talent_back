using AiTalent.Domain.Enums;

namespace AiTalent.Domain.Entities;

/// <summary>Miembro del personal de una empresa (tabla app.interviewers). UserId es el id de Supabase Auth.</summary>
public sealed class StaffMember
{
    private StaffMember()
    {
        Name = string.Empty;
    }

    public StaffMember(Guid userId, Guid companyId, string name, StaffRole role)
    {
        UserId = userId;
        CompanyId = companyId;
        Name = name;
        Role = role;
        Active = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; }

    public StaffRole Role { get; private set; }

    public bool Active { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void ChangeRole(StaffRole role) => Role = role;

    public void SetActive(bool active) => Active = active;
}
