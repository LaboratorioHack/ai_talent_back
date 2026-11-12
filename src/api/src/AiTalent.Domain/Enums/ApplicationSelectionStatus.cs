namespace AiTalent.Domain.Enums;

/// <summary>Selection pipeline status of an application (table app.applications).</summary>
public enum ApplicationSelectionStatus
{
    Applied = 1,
    Shortlisted = 2,
    Interview = 3,
    Rejected = 4,
    Hired = 5,
    Withdrawn = 6,
}
