using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;

namespace AiTalent.Application.Features.Evaluations;

public sealed record ApplicationEvaluation(
    Guid VacancyId,
    Guid ApplicationId,
    Guid ResumeId,
    Guid RoleProfileId,
    CandidateEvaluationResponse Evaluation);

public sealed record RankedApplication(
    int Position,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    Guid ResumeId,
    Guid EvaluationId,
    float TotalScore,
    float Confidence,
    string Fulfillment,
    bool RecommendInterview);

public sealed record VacancyRanking(
    Guid VacancyId,
    Guid RoleProfileId,
    int ProfileVersion,
    IReadOnlyList<RankedApplication> Ranked,
    IReadOnlyList<Guid> NotRanked);

public sealed record ComparedApplication(Guid ApplicationId, Guid ResumeId, string CandidateName);

public sealed record ApplicationComparison(
    Guid VacancyId,
    IReadOnlyList<ComparedApplication> Candidates,
    CompareCandidatesResponse Comparison);

public sealed record ApplicationEvidence(Guid ApplicationId, EvidenceSearchResponse Search);

/// <summary>
/// Staff-facing wrappers over the AI Engine (evaluate, rank, compare, evidence). The engine stays on the
/// internal network with its own key; the API resolves applications to resumes and scopes everything by company.
/// </summary>
public sealed class EvaluationService(
    IJobOpeningRepository vacancies,
    IApplicationRepository applications,
    IAiEngineClient engine,
    StaffAccess access,
    IAuditLog audit)
{
    public const int MaxRankSize = 100;
    public const int MinCompare = 2;
    public const int MaxCompare = 3;
    public const int MaxEvidenceLimit = 20;
    public const int MaxQueryLength = 500;

    public async Task<Result<ApplicationEvaluation>> EvaluateAsync(
        Actor actor,
        Guid vacancyId,
        Guid applicationId,
        CancellationToken ct = default)
    {
        var context = await LoadContextAsync(actor, vacancyId, ct);
        if (!context.IsSuccess)
        {
            return Result<ApplicationEvaluation>.Failure(context.Error!);
        }

        var (vacancy, profile, submissions) = context.Value!;
        var submission = submissions.FirstOrDefault(item => item.ApplicationId == applicationId);
        if (submission is null)
        {
            return Result<ApplicationEvaluation>.Failure(ErrorKind.NotFound, "Application not found.");
        }

        if (!IsReady(submission))
        {
            return Result<ApplicationEvaluation>.Failure(ErrorKind.Conflict, NotReadyMessage);
        }

        var call = await CallEngineAsync(
            () => engine.EvaluateAsync(
                new EvaluateCandidateRequest(
                    profile.Id,
                    submission.ResumeId!.Value,
                    submission.ApplicationId,
                    vacancy.Id,
                    submission.CandidateId),
                ct),
            ct);
        if (!call.IsSuccess)
        {
            return Result<ApplicationEvaluation>.Failure(call.Error!);
        }

        await audit.RecordAsync(
            actor.UserId,
            "evaluation.requested",
            "application",
            submission.ApplicationId.ToString(),
            new { vacancyId = vacancy.Id, roleProfileId = profile.Id, evaluationId = call.Value!.EvaluationId },
            ct);

        return Result<ApplicationEvaluation>.Success(new ApplicationEvaluation(
            vacancy.Id,
            submission.ApplicationId,
            submission.ResumeId!.Value,
            profile.Id,
            call.Value));
    }

    public async Task<Result<VacancyRanking>> RankAsync(
        Actor actor,
        Guid vacancyId,
        IReadOnlyList<Guid>? applicationIds,
        CancellationToken ct = default)
    {
        var context = await LoadContextAsync(actor, vacancyId, ct);
        if (!context.IsSuccess)
        {
            return Result<VacancyRanking>.Failure(context.Error!);
        }

        var (vacancy, profile, submissions) = context.Value!;
        IReadOnlyList<ApplicationSubmission> pool;
        if (applicationIds is { Count: > 0 })
        {
            var selected = Select(submissions, applicationIds, out var selectionError);
            if (selectionError is not null)
            {
                return Result<VacancyRanking>.Failure(selectionError);
            }

            pool = selected;
        }
        else
        {
            pool = submissions.Where(item => IsReady(item) && IsInPlay(item)).ToList();
        }

        if (pool.Count > MaxRankSize)
        {
            return Result<VacancyRanking>.Failure(
                ErrorKind.Invalid,
                $"At most {MaxRankSize} applications can be ranked at once.");
        }

        if (pool.Count == 0)
        {
            return Result<VacancyRanking>.Success(new VacancyRanking(vacancy.Id, profile.Id, profile.Version, [], []));
        }

        var call = await CallEngineAsync(
            () => engine.RankCandidatesAsync(
                new RankCandidatesRequest(profile.Id, pool.Select(item => item.ResumeId!.Value).ToList()),
                ct),
            ct);
        if (!call.IsSuccess)
        {
            return Result<VacancyRanking>.Failure(call.Error!);
        }

        var byResume = pool.ToDictionary(item => item.ResumeId!.Value);
        var ranked = new List<RankedApplication>();
        foreach (var row in call.Value!.Ranked)
        {
            if (!byResume.TryGetValue(row.ResumeId, out var submission))
            {
                continue;
            }

            ranked.Add(new RankedApplication(
                ranked.Count + 1,
                submission.ApplicationId,
                submission.CandidateId,
                submission.CandidateName,
                row.ResumeId,
                row.EvaluationId,
                row.TotalScore,
                row.Confidence,
                row.Fulfillment,
                row.RecommendInterview));
        }

        var rankedIds = ranked.Select(item => item.ApplicationId).ToHashSet();
        var notRanked = pool.Where(item => !rankedIds.Contains(item.ApplicationId)).Select(item => item.ApplicationId).ToList();

        await audit.RecordAsync(
            actor.UserId,
            "ranking.generated",
            "job_opening",
            vacancy.Id.ToString(),
            new { roleProfileId = profile.Id, ranked = ranked.Count, notRanked = notRanked.Count },
            ct);

        return Result<VacancyRanking>.Success(new VacancyRanking(vacancy.Id, profile.Id, profile.Version, ranked, notRanked));
    }

    public async Task<Result<ApplicationComparison>> CompareAsync(
        Actor actor,
        Guid vacancyId,
        IReadOnlyList<Guid>? applicationIds,
        CancellationToken ct = default)
    {
        var ids = applicationIds?.Distinct().ToList() ?? [];
        if (ids.Count < MinCompare || ids.Count > MaxCompare || ids.Count != applicationIds!.Count)
        {
            return Result<ApplicationComparison>.Failure(
                ErrorKind.Invalid,
                $"Send {MinCompare} to {MaxCompare} different applicationIds to compare.");
        }

        var context = await LoadContextAsync(actor, vacancyId, ct);
        if (!context.IsSuccess)
        {
            return Result<ApplicationComparison>.Failure(context.Error!);
        }

        var (vacancy, profile, submissions) = context.Value!;
        var selected = Select(submissions, ids, out var selectionError);
        if (selectionError is not null)
        {
            return Result<ApplicationComparison>.Failure(selectionError);
        }

        var call = await CallEngineAsync(
            () => engine.CompareAsync(
                new CompareCandidatesRequest(profile.Id, selected.Select(item => item.ResumeId!.Value).ToList()),
                ct),
            ct);
        if (!call.IsSuccess)
        {
            return Result<ApplicationComparison>.Failure(call.Error!);
        }

        await audit.RecordAsync(
            actor.UserId,
            "candidates.compared",
            "job_opening",
            vacancy.Id.ToString(),
            new { roleProfileId = profile.Id, applicationIds = ids },
            ct);

        return Result<ApplicationComparison>.Success(new ApplicationComparison(
            vacancy.Id,
            selected.Select(item => new ComparedApplication(item.ApplicationId, item.ResumeId!.Value, item.CandidateName)).ToList(),
            call.Value!));
    }

    public async Task<Result<ApplicationEvidence>> SearchEvidenceAsync(
        Actor actor,
        Guid vacancyId,
        Guid applicationId,
        string? query,
        int? limit,
        CancellationToken ct = default)
    {
        var cleanQuery = query?.Trim() ?? string.Empty;
        if (cleanQuery.Length < 2 || cleanQuery.Length > MaxQueryLength)
        {
            return Result<ApplicationEvidence>.Failure(
                ErrorKind.Invalid,
                $"query must be between 2 and {MaxQueryLength} characters.");
        }

        var cleanLimit = limit ?? 8;
        if (cleanLimit < 1 || cleanLimit > MaxEvidenceLimit)
        {
            return Result<ApplicationEvidence>.Failure(
                ErrorKind.Invalid,
                $"limit must be between 1 and {MaxEvidenceLimit}.");
        }

        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<ApplicationEvidence>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, vacancyId, ct);
        if (vacancy is null)
        {
            return Result<ApplicationEvidence>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        var submissions = await applications.ListByJobOpeningAsync(vacancy.Id, ct);
        var submission = submissions.FirstOrDefault(item => item.ApplicationId == applicationId);
        if (submission is null)
        {
            return Result<ApplicationEvidence>.Failure(ErrorKind.NotFound, "Application not found.");
        }

        if (!IsReady(submission))
        {
            return Result<ApplicationEvidence>.Failure(ErrorKind.Conflict, NotReadyMessage);
        }

        var call = await CallEngineAsync(
            () => engine.SearchEvidenceAsync(
                new EvidenceSearchRequest(submission.ResumeId!.Value, cleanQuery, cleanLimit),
                ct),
            ct);
        if (!call.IsSuccess)
        {
            return Result<ApplicationEvidence>.Failure(call.Error!);
        }

        // The query can describe the candidate; only its length goes to the audit log.
        await audit.RecordAsync(
            actor.UserId,
            "evidence.searched",
            "application",
            submission.ApplicationId.ToString(),
            new { vacancyId = vacancy.Id, queryLength = cleanQuery.Length, chunks = call.Value!.Chunks.Count },
            ct);

        return Result<ApplicationEvidence>.Success(new ApplicationEvidence(submission.ApplicationId, call.Value));
    }

    private const string NotReadyMessage = "The CV of this application has not been processed yet.";

    private static bool IsReady(ApplicationSubmission submission) =>
        submission.ResumeId is not null
        && string.Equals(submission.ProcessingStatus, "PROCESSED", StringComparison.OrdinalIgnoreCase);

    private static bool IsInPlay(ApplicationSubmission submission) =>
        submission.SelectionStatus is not ("WITHDRAWN" or "REJECTED");

    private static List<ApplicationSubmission> Select(
        IReadOnlyList<ApplicationSubmission> submissions,
        IReadOnlyList<Guid> applicationIds,
        out AppError? error)
    {
        var byId = submissions.ToDictionary(item => item.ApplicationId);
        var selected = new List<ApplicationSubmission>();
        foreach (var id in applicationIds.Distinct())
        {
            if (!byId.TryGetValue(id, out var submission))
            {
                error = new AppError(ErrorKind.NotFound, $"Application {id} not found in this vacancy.");
                return [];
            }

            if (!IsReady(submission))
            {
                error = new AppError(ErrorKind.Conflict, $"The CV of application {id} has not been processed yet.");
                return [];
            }

            selected.Add(submission);
        }

        error = null;
        return selected;
    }

    private async Task<Result<EvaluationContext>> LoadContextAsync(Actor actor, Guid vacancyId, CancellationToken ct)
    {
        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<EvaluationContext>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, vacancyId, ct);
        if (vacancy is null)
        {
            return Result<EvaluationContext>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        var profile = vacancy.CurrentProfile;
        if (profile is null || profile.Requirements.Count == 0)
        {
            return Result<EvaluationContext>.Failure(
                ErrorKind.Invalid,
                "The vacancy has no role profile with requirements to evaluate against.");
        }

        var submissions = await applications.ListByJobOpeningAsync(vacancy.Id, ct);
        return Result<EvaluationContext>.Success(new EvaluationContext(vacancy, profile, submissions));
    }

    private static Task<Result<T>> CallEngineAsync<T>(Func<Task<T>> call, CancellationToken ct) =>
        EngineCall.RunAsync(
            call,
            "The AI engine could not process this request with the current CV and role profile.",
            ct);

    private sealed record EvaluationContext(
        JobOpening Vacancy,
        RoleProfile Profile,
        IReadOnlyList<ApplicationSubmission> Submissions);
}
