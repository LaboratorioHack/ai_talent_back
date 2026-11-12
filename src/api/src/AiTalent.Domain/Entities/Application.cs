using AiTalent.Domain.Enums;

namespace AiTalent.Domain.Entities;

/// <summary>Candidate application to a job opening (table app.applications).</summary>
public sealed class Application
{
    private Application()
    {
    }

    public Application(Guid jobOpeningId, Guid candidateId, ApplicationSource source)
    {
        Id = Guid.NewGuid();
        JobOpeningId = jobOpeningId;
        CandidateId = candidateId;
        Source = source;
        SelectionStatus = ApplicationSelectionStatus.Applied;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid JobOpeningId { get; private set; }

    public Guid CandidateId { get; private set; }

    public ApplicationSource Source { get; private set; }

    public ApplicationSelectionStatus SelectionStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void ChangeSelectionStatus(ApplicationSelectionStatus status)
    {
        SelectionStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
