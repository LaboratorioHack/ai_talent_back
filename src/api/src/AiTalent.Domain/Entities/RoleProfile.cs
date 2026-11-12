namespace AiTalent.Domain.Entities;

/// <summary>
/// Perfil de cargo versionado de una vacante (tabla app.role_profiles).
/// Una vez usado en una evaluación no se modifica: se crea una versión nueva.
/// </summary>
public sealed class RoleProfile
{
    private RoleProfile()
    {
        EvaluationPolicy = "{}";
    }

    public RoleProfile(Guid jobOpeningId, int version, string? description)
    {
        Id = Guid.NewGuid();
        JobOpeningId = jobOpeningId;
        Version = version;
        Description = description;
        EvaluationPolicy = "{}";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid JobOpeningId { get; private set; }

    public int Version { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Política de evaluación (jsonb). Reservada para umbrales y reglas por perfil.</summary>
    public string EvaluationPolicy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public List<Requirement> Requirements { get; private set; } = [];

    public decimal WeightTotal => Requirements.Sum(requirement => requirement.Weight);

    public void Update(string? description) => Description = description;

    public void ReplaceRequirements(IEnumerable<Requirement> requirements)
    {
        Requirements.Clear();
        Requirements.AddRange(requirements);
    }
}
