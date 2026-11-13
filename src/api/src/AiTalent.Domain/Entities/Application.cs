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

    public bool CanMoveTo(ApplicationSelectionStatus status) =>
        Transitions.TryGetValue(SelectionStatus, out var allowed) && allowed.Contains(status);

    public bool MoveTo(ApplicationSelectionStatus status)
    {
        if (!CanMoveTo(status))
        {
            return false;
        }

        SelectionStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    private static readonly Dictionary<ApplicationSelectionStatus, ApplicationSelectionStatus[]> Transitions = new()
    {
        [ApplicationSelectionStatus.Applied] =
        [
            ApplicationSelectionStatus.Shortlisted,
            ApplicationSelectionStatus.Rejected,
            ApplicationSelectionStatus.Withdrawn,
        ],
        // One step back is allowed between open stages, so a recruiter can undo a move.
        [ApplicationSelectionStatus.Shortlisted] =
        [
            ApplicationSelectionStatus.Applied,
            ApplicationSelectionStatus.Interview,
            ApplicationSelectionStatus.Rejected,
            ApplicationSelectionStatus.Withdrawn,
        ],
        [ApplicationSelectionStatus.Interview] =
        [
            ApplicationSelectionStatus.Shortlisted,
            ApplicationSelectionStatus.Hired,
            ApplicationSelectionStatus.Rejected,
            ApplicationSelectionStatus.Withdrawn,
        ],
    };
}
