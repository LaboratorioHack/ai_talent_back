using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Rankings;

public sealed class RankingService(
    IJobOpeningRepository vacancies,
    IEvaluationReadStore evaluations,
    StaffAccess access)
{
    public async Task<Result<IReadOnlyList<RankingSummaryResponse>>> ListSummariesAsync(
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<IReadOnlyList<RankingSummaryResponse>>.Failure(member.Error!);
        }

        var stats = await evaluations.ListOpenVacancyStatsAsync(member.Value!.CompanyId, ct);
        var summaries = new List<RankingSummaryResponse>();
        foreach (var row in stats)
        {
            var evaluated = row.Meets + row.Partial + row.DoesNotMeet;
            IReadOnlyList<RankingTopEntryResponse> top = [];
            if (row.ProfileVersion > 0 && evaluated > 0)
            {
                var vacancy = await vacancies.FindByIdAsync(member.Value.CompanyId, row.VacancyId, ct);
                var profile = vacancy?.CurrentProfile;
                if (profile is not null)
                {
                    var snapshots = await evaluations.ListLatestCompletedAsync(row.VacancyId, profile.Id, ct);
                    var ordered = Order(snapshots);
                    top = ordered
                        .Take(3)
                        .Select(snapshot => new RankingTopEntryResponse(
                            snapshot.ApplicationId,
                            snapshot.CandidateName,
                            (double)snapshot.TotalScore))
                        .ToList();
                }
            }

            summaries.Add(new RankingSummaryResponse(
                row.VacancyId,
                row.Title,
                RankingMapping.ToIsoDate(row.ClosingAt),
                evaluated,
                row.Meets,
                row.Partial,
                row.DoesNotMeet,
                top));
        }

        return Result<IReadOnlyList<RankingSummaryResponse>>.Success(summaries);
    }

    public async Task<Result<IReadOnlyList<TopPersonResponse>>> ListTopPeopleAsync(
        Guid actorUserId,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<IReadOnlyList<TopPersonResponse>>.Failure(member.Error!);
        }

        var stats = await evaluations.ListOpenVacancyStatsAsync(member.Value!.CompanyId, ct);
        var people = new List<TopPersonResponse>();
        foreach (var row in stats.Where(stat => stat.Received > 0))
        {
            var vacancy = await vacancies.FindByIdAsync(member.Value.CompanyId, row.VacancyId, ct);
            var profile = vacancy?.CurrentProfile;
            if (profile is null)
            {
                continue;
            }

            var requirements = profile.Requirements;
            var snapshots = Order(await evaluations.ListLatestCompletedAsync(row.VacancyId, profile.Id, ct));
            foreach (var snapshot in snapshots.Take(6))
            {
                var mapped = RankingMapping.MapCandidate(snapshot, requirements, 0);
                var met = mapped.Results
                    .Where(result => result.Result == "MEETS")
                    .Select(result => requirements.FirstOrDefault(req => req.Id == result.RequirementId)?.Description)
                    .Where(description => description is not null)
                    .Cast<string>()
                    .ToList();
                var missing = mapped.Results
                    .Where(result => result.Result is "PARTIAL" or "DOES_NOT_MEET")
                    .Select(result => requirements.FirstOrDefault(req => req.Id == result.RequirementId)?.Description)
                    .Where(description => description is not null)
                    .Cast<string>()
                    .ToList();
                var highlights = mapped.Results
                    .Where(result => result.Result == "MEETS" && result.Evidence.Count > 0)
                    .OrderByDescending(result => result.Score)
                    .Take(2)
                    .Select(result => new HighlightResponse(
                        requirements.FirstOrDefault(req => req.Id == result.RequirementId)?.Description ?? "Requisito",
                        result.Evidence[0].Citation))
                    .ToList();

                people.Add(new TopPersonResponse(
                    snapshot.ApplicationId,
                    snapshot.CandidateName,
                    mapped.Score,
                    mapped.Fulfillment,
                    mapped.Confidence,
                    row.VacancyId,
                    row.Title,
                    highlights,
                    met,
                    missing));
            }
        }

        return Result<IReadOnlyList<TopPersonResponse>>.Success(
            people.OrderByDescending(person => person.Score).ToList());
    }

    public async Task<Result<RankingDataResponse>> GetByVacancyAsync(
        Guid actorUserId,
        Guid vacancyId,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<RankingDataResponse>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, vacancyId, ct);
        if (vacancy is null)
        {
            return Result<RankingDataResponse>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        var profile = vacancy.CurrentProfile;
        var requirements = profile?.Requirements ?? [];
        var openVacancies = await vacancies.ListByCompanyAsync(
            member.Value.CompanyId,
            JobOpeningStatus.Open,
            search: null,
            skip: 0,
            take: 100,
            ct);
        var jobOptions = new List<JobOptionResponse>();
        foreach (var open in openVacancies)
        {
            var stat = (await evaluations.ListOpenVacancyStatsAsync(member.Value.CompanyId, ct))
                .FirstOrDefault(row => row.VacancyId == open.Id);
            var evaluated = (stat?.Meets ?? 0) + (stat?.Partial ?? 0) + (stat?.DoesNotMeet ?? 0);
            jobOptions.Add(new JobOptionResponse(
                open.Id.ToString(),
                open.Title,
                evaluated > 0 ? $"{evaluated} evaluadas" : null));
        }

        if (profile is null)
        {
            return Result<RankingDataResponse>.Success(new RankingDataResponse(
                new RankingJobResponse(vacancy.Id, vacancy.Title, RankingMapping.ToIsoDate(vacancy.ClosingAt), 0, 0),
                jobOptions,
                [],
                []));
        }

        var stats = (await evaluations.ListOpenVacancyStatsAsync(member.Value.CompanyId, ct))
            .FirstOrDefault(row => row.VacancyId == vacancyId);
        var snapshots = Order(await evaluations.ListLatestCompletedAsync(vacancyId, profile.Id, ct));

        var candidates = snapshots
            .Select((snapshot, index) => RankingMapping.MapCandidate(snapshot, requirements, index + 1))
            .ToList();

        return Result<RankingDataResponse>.Success(new RankingDataResponse(
            new RankingJobResponse(
                vacancy.Id,
                vacancy.Title,
                RankingMapping.ToIsoDate(vacancy.ClosingAt),
                profile.Version,
                stats?.Received ?? 0),
            jobOptions,
            RankingMapping.MapRequirements(requirements),
            candidates));
    }

    /// <summary>
    /// Reading a ranking never calls the AI: it orders the evaluations already stored (disqualified last, then
    /// score and confidence). Evaluating is the explicit POST /vacancies/{id}/rank, which uses Jev and Claude.
    /// </summary>
    internal static IReadOnlyList<VacancyEvaluationSnapshot> Order(IReadOnlyList<VacancyEvaluationSnapshot> snapshots) =>
        snapshots
            .OrderBy(snapshot => snapshot.Disqualified)
            .ThenByDescending(snapshot => snapshot.TotalScore)
            .ThenByDescending(snapshot => snapshot.Confidence)
            .ToList();
}
