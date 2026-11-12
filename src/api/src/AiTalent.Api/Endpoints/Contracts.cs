namespace AiTalent.Api.Endpoints;

internal sealed record CreateCompanyRequest(string? CompanyName, string? Sector, string? Size, string? YourName);

internal sealed record JoinCompanyRequest(string? Code, string? YourName);

internal sealed record UpdateStaffRequest(string? Role, bool? Active);

internal sealed record CompanyResponse(Guid Id, string Name, string? Sector, string? Size, string Role);

internal sealed record InviteCodeResponse(string Code, DateTimeOffset ExpiresAt);

internal sealed record StaffResponse(Guid UserId, string Name, string Role, bool Active);

internal sealed record MeResponse(Guid UserId, string? Email, string? Name, bool MfaVerified, CompanyResponse? Company);

internal sealed record VacancyPageResponse(
    IReadOnlyList<VacancySummaryResponse> Items,
    int Page,
    int PageSize,
    int Total);

internal sealed record VacancySummaryResponse(
    Guid Id,
    string Title,
    string? Area,
    string Status,
    DateTimeOffset? ClosingAt,
    int Positions,
    int? ProfileVersion,
    VacancyCountsResponse Counts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record VacancyDetailResponse(
    Guid Id,
    string Title,
    string? Area,
    string Status,
    DateTimeOffset? ClosingAt,
    int Positions,
    Guid? ProfileId,
    int? ProfileVersion,
    string? Description,
    decimal WeightTotal,
    bool ProfileInUse,
    IReadOnlyList<RequirementResponse> Requirements,
    VacancyCountsResponse Counts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record VacancyCountsResponse(
    int Resumes,
    int Meets,
    int PartiallyMeets,
    int DoesNotMeet,
    int Disqualified,
    int Interviews);

internal sealed record RequirementResponse(
    Guid Id,
    string Type,
    string? Category,
    string Description,
    string? Condition,
    decimal Weight,
    IReadOnlyDictionary<string, int> Equivalences);

internal sealed record CreateVacancyRequest(
    string? Title,
    string? Area,
    DateTimeOffset? ClosingAt,
    int? Positions,
    string? Description,
    IReadOnlyList<RequirementRequest>? Requirements);

internal sealed record UpdateVacancyRequest(
    string? Title,
    string? Area,
    DateTimeOffset? ClosingAt,
    int? Positions);

internal sealed record UpdateVacancyProfileRequest(
    string? Description,
    IReadOnlyList<RequirementRequest>? Requirements);

internal sealed record RequirementRequest(
    string? Type,
    string? Category,
    string? Description,
    string? Condition,
    decimal? Weight,
    IReadOnlyDictionary<string, int>? Equivalences);

internal sealed record CandidateApplicationResponse(
    Guid ApplicationId,
    Guid CandidateId,
    Guid ResumeId,
    string Status,
    bool DuplicateContent,
    string Message);

internal sealed record PublicVacancyResponse(
    Guid Id,
    string Title,
    string? Area,
    DateTimeOffset? ClosingAt,
    int Positions,
    string CompanyName,
    string? Description,
    IReadOnlyList<PublicRequirementResponse> Requirements);

internal sealed record PublicRequirementResponse(
    string Type,
    string? Category,
    string Description,
    string? Condition);

internal sealed record ChangeApplicationStatusRequest(string? Status, string? Reason);

internal sealed record ApplicationStageResponse(
    Guid ApplicationId,
    Guid VacancyId,
    Guid CandidateId,
    string SelectionStatus,
    DateTimeOffset UpdatedAt);

internal sealed record VacancyApplicationResponse(
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

internal sealed record RankApplicationsRequest(IReadOnlyList<Guid>? ApplicationIds);

internal sealed record CompareApplicationsRequest(IReadOnlyList<Guid>? ApplicationIds);

internal sealed record EvidenceSearchApiRequest(string? Query, int? Limit);
