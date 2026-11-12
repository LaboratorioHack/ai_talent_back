using AiTalent.Domain.Enums;

namespace AiTalent.Domain.Entities;

/// <summary>Vacante de una empresa (tabla app.job_openings).</summary>
public sealed class JobOpening
{
    private JobOpening()
    {
        Title = string.Empty;
    }

    public JobOpening(
        Guid companyId,
        Guid? creatorId,
        string title,
        string? area,
        DateTimeOffset? closingAt,
        int positions)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        CreatorId = creatorId;
        Title = title;
        Area = area;
        ClosingAt = closingAt;
        Positions = positions;
        Status = JobOpeningStatus.Draft;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid? CreatorId { get; private set; }

    public string Title { get; private set; }

    public string? Area { get; private set; }

    public DateTimeOffset? ClosingAt { get; private set; }

    public int Positions { get; private set; }

    public JobOpeningStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Borrado lógico: conserva postulaciones, evaluaciones y auditoría.</summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    public List<RoleProfile> Profiles { get; private set; } = [];

    public bool IsDeleted => DeletedAt is not null;

    public bool IsAcceptingApplications(DateTimeOffset now) =>
        DeletedAt is null
        && Status == JobOpeningStatus.Open
        && (ClosingAt is null || ClosingAt > now);

    public RoleProfile? CurrentProfile =>
        Profiles.Count == 0 ? null : Profiles.MaxBy(profile => profile.Version);

    public void Update(string title, string? area, DateTimeOffset? closingAt, int positions)
    {
        Title = title;
        Area = area;
        ClosingAt = closingAt;
        Positions = positions;
        Touch();
    }

    public void Publish()
    {
        Status = JobOpeningStatus.Open;
        Touch();
    }

    public void Close()
    {
        Status = JobOpeningStatus.Closed;
        Touch();
    }

    public void Delete()
    {
        DeletedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
