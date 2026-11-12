namespace AiTalent.Domain.Entities;

/// <summary>Candidate PII, scoped to a company (table app.candidates). Never sent to LLMs or embeddings.</summary>
public sealed class Candidate
{
    private Candidate()
    {
        Name = string.Empty;
        ContactEmail = string.Empty;
    }

    public Candidate(Guid companyId, string name, string contactEmail)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        Name = name;
        ContactEmail = contactEmail;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Optional Supabase Auth user, set when the candidate logs in (e.g. interview).</summary>
    public Guid? UserId { get; private set; }

    public string Name { get; private set; }

    public string ContactEmail { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateName(string name) => Name = name;
}
