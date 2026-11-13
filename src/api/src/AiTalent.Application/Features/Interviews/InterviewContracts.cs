namespace AiTalent.Application.Features.Interviews;

public sealed record InterviewResponse(
    Guid Id,
    Guid EvaluationId,
    CandidateResponse Candidate,
    string JobTitle,
    string Status,
    int DurationMin,
    string StartedAt,
    int TotalQuestions,
    IReadOnlyList<InterviewTurnResponse> Turns,
    IReadOnlyList<InterviewContrastResponse> Contrasts,
    string? Recommendation,
    Guid ApplicationId,
    Guid VacancyId,
    string? ExpiresAt);

public sealed record CandidateResponse(Guid Id, string Name, string ContactEmail);

public sealed record InterviewTurnResponse(
    int Order,
    string Topic,
    string Question,
    string? Answer,
    string? Verdict);

public sealed record InterviewContrastResponse(
    string Label,
    string CvSays,
    string InterviewSays,
    string Verdict);
