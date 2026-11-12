namespace AiTalent.Application.Abstractions.AiEngine;

public sealed record IngestCvRequest(Guid ResumeId, bool FromStorage = true);

public sealed record CvIngestionResponse(
    Guid ResumeId,
    string ExtractorVersion,
    string ParserStrategy,
    float? OcrConfidence,
    int ChunksCount,
    StructuredCandidateProfile? ProfessionalData);

public sealed record AttributesSanitized(
    int NamesRemoved = 0,
    int ContactInfoRemoved = 0,
    int GenderIndicatorsDetected = 0,
    int LocationIndicatorsRemoved = 0,
    int AgeOrDobIndicatorsRemoved = 0);

public sealed record BiasGuardrailTelemetry(
    bool PiiScrubbed,
    AttributesSanitized AttributesSanitized,
    IReadOnlyList<string> ComplianceFlags);

public sealed record StructuredCandidateProfile(
    string Summary,
    int? TotalExperienceMonths,
    string? SeniorityLevel,
    IReadOnlyList<string> Skills,
    string ParserStrategy,
    float? OcrConfidence,
    string ExtractorVersion,
    BiasGuardrailTelemetry? BiasGuardrailTelemetry = null);

public sealed record EvaluateCandidateRequest(
    Guid RoleProfileId,
    Guid ResumeId,
    Guid? ApplicationId = null,
    Guid? JobOpeningId = null,
    Guid? CandidateId = null);

public sealed record EvidenceChunkDto(
    Guid FragmentId,
    string? Section,
    string Citation,
    int? PageNumber,
    float? Similarity);

public sealed record RequirementAssessmentDto(
    Guid RequirementId,
    string Result,
    float Score,
    float Confidence,
    string Justification,
    IReadOnlyList<EvidenceChunkDto> Evidence);

public sealed record FastPassInfoDto(
    bool Used,
    bool SkippedClaude,
    string? Model = null,
    float? Certainty = null);

public sealed record CandidateEvaluationResponse(
    Guid EvaluationId,
    float TotalScore,
    string Fulfillment,
    float Confidence,
    bool RecommendInterview,
    bool Disqualified,
    IReadOnlyList<object> Strengths,
    IReadOnlyList<object> Gaps,
    IReadOnlyList<RequirementAssessmentDto> RequirementAssessments,
    string AiModel,
    string PromptVersion,
    string ReasoningSummary,
    FastPassInfoDto? FastPass = null);

public sealed record RankCandidatesRequest(Guid RoleProfileId, IReadOnlyList<Guid> ResumeIds);

public sealed record RankedCandidateDto(
    Guid ResumeId,
    Guid EvaluationId,
    float TotalScore,
    float Confidence,
    string Fulfillment,
    bool RecommendInterview);

public sealed record RankCandidatesResponse(Guid RoleProfileId, IReadOnlyList<RankedCandidateDto> Ranked);

public sealed record EvidenceSearchRequest(Guid ResumeId, string Query, int Limit = 8);

public sealed record EvidenceSearchResponse(Guid ResumeId, string Query, IReadOnlyList<EvidenceChunkDto> Chunks);

public sealed record CompareCandidatesRequest(Guid RoleProfileId, IReadOnlyList<Guid> ResumeIds);

public sealed record SideBySideCandidateDto(
    string CandidateId,
    string PrimaryAdvantage,
    string PrimaryTradeOffOrGap,
    string IdealTeamFit);

public sealed record InterviewFocusDto(
    string CandidateId,
    string TargetGapToVerify,
    string SuggestedInterviewQuestion);

public sealed record CompareCandidatesResponse(
    Guid RoleProfileId,
    string ComparativeSummary,
    IReadOnlyList<SideBySideCandidateDto> SideBySideMatrix,
    string HeadToHeadTradeOff,
    IReadOnlyList<InterviewFocusDto> RecommendedInterviewFocus);
