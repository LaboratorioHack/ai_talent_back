namespace AiTalent.Application.Abstractions;

public sealed record InterviewSnapshot(
    Guid InterviewId,
    Guid EvaluationId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    string JobTitle,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? Recommendation,
    string? InconsistenciesJson,
    IReadOnlyList<InterviewTurnSnapshot> Turns);

public sealed record InterviewTurnSnapshot(
    int TurnOrder,
    string? Topic,
    string Question,
    string? Answer,
    string? AnswerEvaluation);

public interface IInterviewReadStore
{
    Task<IReadOnlyList<InterviewSnapshot>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default);

    Task<InterviewSnapshot?> FindByIdAsync(Guid companyId, Guid interviewId, CancellationToken ct = default);
}
