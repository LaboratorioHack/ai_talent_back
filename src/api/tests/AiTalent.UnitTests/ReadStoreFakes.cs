using AiTalent.Application.Abstractions;

namespace AiTalent.UnitTests;

internal sealed class FakeEvaluationReadStore : IEvaluationReadStore
{
    public List<VacancyEvaluationStats> Stats { get; } = [];
    public Dictionary<(Guid VacancyId, Guid ProfileId), List<VacancyEvaluationSnapshot>> ByVacancy { get; } = [];
    public GovernanceEvaluationStats GovernanceStats { get; set; } = new(0, 100, 0);

    public Task<IReadOnlyList<VacancyEvaluationStats>> ListOpenVacancyStatsAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VacancyEvaluationStats>>(Stats);

    public Task<IReadOnlyList<VacancyEvaluationSnapshot>> ListLatestCompletedAsync(
        Guid vacancyId,
        Guid roleProfileId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<VacancyEvaluationSnapshot>>(
            ByVacancy.TryGetValue((vacancyId, roleProfileId), out var list) ? list : []);

    public Task<GovernanceEvaluationStats> GetGovernanceEvaluationStatsAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(GovernanceStats);
}

internal sealed class FakeInterviewReadStore : IInterviewReadStore
{
    public List<InterviewSnapshot> Interviews { get; } = [];

    public Task<IReadOnlyList<InterviewSnapshot>> ListByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<InterviewSnapshot>>(Interviews);

    public Task<InterviewSnapshot?> FindByIdAsync(Guid companyId, Guid interviewId, CancellationToken ct = default) =>
        Task.FromResult(Interviews.FirstOrDefault(interview => interview.InterviewId == interviewId));
}

internal sealed class FakeGovernanceReadStore : IGovernanceReadStore
{
    public List<AuditLogEntry> Entries { get; } = [];
    public int HumanDecisions { get; set; }
    public int AiErrorsFlagged { get; set; }

    public Task<IReadOnlyList<AuditLogEntry>> ListForCompanyAsync(Guid companyId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditLogEntry>>(Entries.Take(limit).ToList());

    public Task<int> CountHumanDecisionsAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(HumanDecisions);

    public Task<int> CountAiErrorsFlaggedAsync(Guid companyId, CancellationToken ct = default) =>
        Task.FromResult(AiErrorsFlagged);
}
