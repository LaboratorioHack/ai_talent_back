namespace AiTalent.Application.Abstractions;

/// <summary>An open vacancy on the public board (every company).</summary>
public sealed record PublicJobRow(
    Guid Id,
    string Title,
    string Company,
    string? Area,
    string? Location,
    string? Modality,
    DateTimeOffset? ClosingAt,
    DateTimeOffset? PublishedAt);

/// <summary>A requirement of the current profile of a public vacancy. `Equivalences` holds the accepted names only.</summary>
public sealed record PublicJobRequirementRow(
    Guid JobId,
    string Type,
    string Description,
    IReadOnlyList<string> Equivalences);

/// <summary>One application of the person behind an e-mail, with what the portal shows about it.</summary>
public sealed record ApplicantApplicationRow(
    Guid ApplicationId,
    Guid JobId,
    string JobTitle,
    string Company,
    string? JobArea,
    string? Location,
    string? Modality,
    string CandidateName,
    string SelectionStatus,
    DateTimeOffset AppliedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? EvaluatedAt,
    string? InterviewStatus,
    string? InterviewToken,
    DateTimeOffset? InterviewCreatedAt,
    DateTimeOffset? InterviewExpiresAt);

/// <summary>Latest CV of the person behind an e-mail.</summary>
public sealed record ApplicantResumeRow(
    Guid ResumeId,
    string CandidateName,
    string FileKey,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    DateTimeOffset CreatedAt,
    string? ProfessionalData);

/// <summary>Who gets invited to the AI interview, scoped to the recruiter's company.</summary>
public sealed record InterviewTarget(
    Guid ApplicationId,
    Guid VacancyId,
    string VacancyTitle,
    string CompanyName,
    string SelectionStatus,
    string CandidateName,
    string CandidateEmail,
    Guid? EvaluationId);

/// <summary>A requirement of the evaluated profile and what the evaluation found for it.</summary>
public sealed record RequirementGapRow(string Description, string? Category, string Type, string? Result);

public sealed record InterviewQuestionSeed(string Topic, string Question);

public sealed record PublicTurnRow(int Order, string Question, string? Answer);

/// <summary>An interview opened through its link token.</summary>
public sealed record PublicInterviewRow(
    Guid InterviewId,
    string Status,
    DateTimeOffset? ExpiresAt,
    string CandidateName,
    string JobTitle,
    string Company,
    IReadOnlyList<PublicTurnRow> Turns);

/// <summary>Who to e-mail about an interview, scoped to the recruiter's company.</summary>
public sealed record InterviewContactRow(
    Guid InterviewId,
    Guid ApplicationId,
    string CandidateName,
    string CandidateEmail,
    string JobTitle,
    string CompanyName);

/// <summary>Something Criba AI did with a CV, newest first, for the home "Criba AI, ahora" log.</summary>
public sealed record AiActivityRow(
    string Kind,
    DateTimeOffset At,
    string JobTitle,
    string? FileName,
    int? Pages,
    string? Fulfillment,
    int? Position,
    int? Of);

/// <summary>Raw SQL reads and writes for the candidate side (public board, applicant portal, interview link).</summary>
public interface ICandidateSideStore
{
    Task<IReadOnlyList<PublicJobRow>> ListPublicJobsAsync(DateTimeOffset now, CancellationToken ct = default);

    Task<IReadOnlyList<PublicJobRequirementRow>> ListPublicRequirementsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);

    Task<IReadOnlyList<ApplicantApplicationRow>> ListApplicantApplicationsAsync(string email, CancellationToken ct = default);

    Task<ApplicantResumeRow?> FindLatestResumeAsync(string email, CancellationToken ct = default);

    /// <summary>Withdraws an open application of this e-mail. False when it does not exist or is already closed.</summary>
    Task<bool> WithdrawAsync(string email, Guid applicationId, CancellationToken ct = default);

    /// <summary>Erases every candidate row of this e-mail (cascades to CVs, applications and evaluations).</summary>
    Task<int> EraseAsync(string email, CancellationToken ct = default);

    Task<InterviewTarget?> FindInterviewTargetAsync(Guid companyId, Guid vacancyId, Guid applicationId, CancellationToken ct = default);

    Task<IReadOnlyList<RequirementGapRow>> ListRequirementGapsAsync(Guid evaluationId, CancellationToken ct = default);

    /// <summary>
    /// Creates the AI interview with its questions, cancels any earlier open one of the same application
    /// and moves the application to INTERVIEW. Returns the interview id.
    /// </summary>
    Task<Guid> CreateInterviewAsync(
        Guid evaluationId,
        Guid applicationId,
        Guid interviewerId,
        string token,
        DateTimeOffset expiresAt,
        string language,
        IReadOnlyList<InterviewQuestionSeed> questions,
        CancellationToken ct = default);

    Task<PublicInterviewRow?> FindInterviewByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Stores (or appends to) the answer of a turn and moves the interview to IN_PROGRESS or COMPLETED.</summary>
    Task<bool> SaveAnswerAsync(Guid interviewId, int order, string answer, CancellationToken ct = default);

    Task<InterviewContactRow?> FindInterviewContactAsync(Guid companyId, Guid interviewId, CancellationToken ct = default);

    /// <summary>Storage key of the latest CV behind an application of the recruiter's company.</summary>
    Task<string?> FindResumeKeyAsync(Guid companyId, Guid vacancyId, Guid applicationId, CancellationToken ct = default);

    Task<IReadOnlyList<AiActivityRow>> ListAiActivityAsync(Guid companyId, int take, CancellationToken ct = default);
}
