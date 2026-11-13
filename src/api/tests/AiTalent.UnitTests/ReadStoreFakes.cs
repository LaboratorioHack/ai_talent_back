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

    public Task<GovernanceEvaluationStats> GetGovernanceEvaluationStatsAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default) =>
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

    public Task<IReadOnlyList<AuditLogEntry>> ListForCompanyAsync(Guid companyId, int limit, DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditLogEntry>>(Entries.Take(limit).ToList());

    public Task<int> CountHumanDecisionsAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult(HumanDecisions);

    public Task<int> CountAiErrorsFlaggedAsync(Guid companyId, DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult(AiErrorsFlagged);
}

/// <summary>Only the dashboard activity is read in unit tests; the rest is SQL covered against a database.</summary>
internal sealed class FakeCandidateSideStore : ICandidateSideStore
{
    public List<AiActivityRow> Activity { get; } = [];

    public Task<IReadOnlyList<AiActivityRow>> ListAiActivityAsync(Guid companyId, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AiActivityRow>>(Activity.Take(take).ToList());

    public Task<IReadOnlyList<PublicJobRow>> ListPublicJobsAsync(DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<PublicJobRequirementRow>> ListPublicRequirementsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ApplicantApplicationRow>> ListApplicantApplicationsAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ApplicantResumeRow?> FindLatestResumeAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<ResumeVersionRow>> ListResumeVersionsAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> WithdrawAsync(string email, Guid applicationId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<int> EraseAsync(string email, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<InterviewTarget?> FindInterviewTargetAsync(Guid companyId, Guid vacancyId, Guid applicationId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<RequirementGapRow>> ListRequirementGapsAsync(Guid evaluationId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<Guid> CreateInterviewAsync(Guid evaluationId, Guid applicationId, Guid interviewerId, string token, DateTimeOffset expiresAt, string language, IReadOnlyList<InterviewQuestionSeed> questions, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<PublicInterviewRow?> FindInterviewByTokenAsync(string token, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> SaveAnswerAsync(Guid interviewId, int order, string answer, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<InterviewContactRow?> FindInterviewContactAsync(Guid companyId, Guid interviewId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<string?> FindResumeKeyAsync(Guid companyId, Guid vacancyId, Guid applicationId, CancellationToken ct = default) => throw new NotSupportedException();
}
