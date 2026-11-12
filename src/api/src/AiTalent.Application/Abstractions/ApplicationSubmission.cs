namespace AiTalent.Application.Abstractions;

public sealed record ApplicationSubmission(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    string Source,
    string SelectionStatus,
    DateTimeOffset AppliedAt,
    DateTimeOffset UpdatedAt,
    Guid? ResumeId,
    int? ResumeVersion,
    string? ProcessingStatus,
    string? ProcessingError,
    DateTimeOffset? ResumeCreatedAt);
