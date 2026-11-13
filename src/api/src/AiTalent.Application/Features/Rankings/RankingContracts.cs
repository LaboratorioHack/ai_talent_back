namespace AiTalent.Application.Features.Rankings;

public sealed record RankingDataResponse(
    RankingJobResponse Job,
    IReadOnlyList<JobOptionResponse> Jobs,
    IReadOnlyList<RankRequirementResponse> Requirements,
    IReadOnlyList<RankedCandidateResponse> Candidates);

public sealed record RankingJobResponse(
    Guid Id,
    string Title,
    string? ClosesAt,
    int ProfileVersion,
    int Received);

public sealed record JobOptionResponse(string Value, string Label, string? Hint);

public sealed record RankRequirementResponse(
    Guid Id,
    string Description,
    string Type,
    decimal? Weight,
    string? Category,
    IReadOnlyList<EquivalenceResponse> Equivalences);

public sealed record EquivalenceResponse(string Label, int Percent);

public sealed record RankedCandidateResponse(
    Guid Id,
    string Name,
    double Score,
    string Fulfillment,
    int MandatoryMet,
    int MandatoryTotal,
    double Confidence,
    string EvaluatedAt,
    string SourceLabel,
    IReadOnlyList<RequirementResultResponse> Results,
    IReadOnlyList<ToValidateResponse> ToValidate,
    ResumeDocResponse Resume);

public sealed record RequirementResultResponse(
    Guid Id,
    Guid RequirementId,
    string Result,
    double Score,
    double Confidence,
    string Justification,
    IReadOnlyList<EvidenceResponse> Evidence);

public sealed record EvidenceResponse(Guid Id, string Citation, string Location);

public sealed record ToValidateResponse(string Label, string Tone);

public sealed record ResumeDocResponse(
    string Name,
    string Headline,
    IReadOnlyList<string> Contact,
    IReadOnlyList<ResumeSectionResponse> Sections,
    int? Pages);

public sealed record ResumeSectionResponse(string Title, IReadOnlyList<ResumeLineResponse> Lines);

public sealed record ResumeLineResponse(string Kind, string Text, string? Right, ResumeNoteResponse? Note);

public sealed record ResumeNoteResponse(string Requirement, int Points, bool? Partial);

public sealed record RankingSummaryResponse(
    Guid Id,
    string Title,
    string? ClosesAt,
    int Evaluated,
    int Meets,
    int Partial,
    int DoesNotMeet,
    IReadOnlyList<RankingTopEntryResponse> Top);

public sealed record RankingTopEntryResponse(Guid Id, string Name, double Score);

public sealed record TopPersonResponse(
    Guid Id,
    string Name,
    double Score,
    string Fulfillment,
    double Confidence,
    Guid JobId,
    string JobTitle,
    IReadOnlyList<HighlightResponse> Highlights,
    IReadOnlyList<string> Met,
    IReadOnlyList<string> Missing);

public sealed record HighlightResponse(string Requirement, string Citation);
