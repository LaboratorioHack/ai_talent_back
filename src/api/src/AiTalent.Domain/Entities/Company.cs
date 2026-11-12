namespace AiTalent.Domain.Entities;

/// <summary>Empresa cliente. Todos los datos de negocio pertenecen a exactamente una empresa.</summary>
public sealed class Company
{
    private Company()
    {
        Name = string.Empty;
    }

    public Company(string name, string? sector, string? size)
    {
        Id = Guid.NewGuid();
        Name = name;
        Sector = sector;
        Size = size;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>Sector de la organización (texto libre). Null en empresas creadas antes del onboarding actual.</summary>
    public string? Sector { get; private set; }

    /// <summary>Tamaño de la organización. Null en empresas creadas antes del onboarding actual.</summary>
    public string? Size { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
