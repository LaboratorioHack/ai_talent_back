namespace AiTalent.Application.Abstractions;

/// <summary>
/// One application of a vacancy with its latest CV. File details, pages and what was read come
/// from the resume row and its extraction; `DuplicateOf` names an earlier application of the same
/// vacancy whose CV has the same hash. `Fulfillment`/`Score` come from the latest completed evaluation.
/// </summary>
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
    DateTimeOffset? ResumeCreatedAt,
    string? FileName = null,
    string? ContentType = null,
    long? SizeBytes = null,
    int? Pages = null,
    int? JobsRead = null,
    int? CertificationsRead = null,
    string? DuplicateOf = null,
    string? Fulfillment = null,
    decimal? Score = null);
