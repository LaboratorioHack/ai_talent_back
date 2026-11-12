using AiTalent.Domain.Enums;

namespace AiTalent.Domain.Entities;

/// <summary>Requisito de un perfil de cargo (tabla app.requirements).</summary>
public sealed class Requirement
{
    private Requirement()
    {
        Description = string.Empty;
        Equivalences = [];
    }

    public Requirement(
        Guid roleProfileId,
        RequirementType type,
        string? category,
        string description,
        string? condition,
        decimal weight,
        IReadOnlyDictionary<string, int>? equivalences)
    {
        Id = Guid.NewGuid();
        RoleProfileId = roleProfileId;
        Type = type;
        Category = category;
        Description = description;
        Condition = condition;
        Weight = weight;
        Equivalences = equivalences is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : equivalences.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    public Guid Id { get; private set; }

    public Guid RoleProfileId { get; private set; }

    public RequirementType Type { get; private set; }

    public string? Category { get; private set; }

    public string Description { get; private set; }

    public string? Condition { get; private set; }

    public decimal Weight { get; private set; }

    /// <summary>Equivalencias y su porcentaje de crédito, p. ej. <c>{"Azure": 80}</c>.</summary>
    public Dictionary<string, int> Equivalences { get; private set; }
}
