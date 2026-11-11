namespace AiTalent.Domain.Entities;

/// <summary>Empresa cliente. Todos los datos de negocio pertenecen a exactamente una empresa.</summary>
public sealed class Company
{
    private Company()
    {
        Name = string.Empty;
    }

    public Company(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
