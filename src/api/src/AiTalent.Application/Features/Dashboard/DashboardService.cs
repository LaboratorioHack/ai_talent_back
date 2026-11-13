using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Dashboard;

/// <summary>Home dashboard and bell notices, computed from applications, resumes and evaluations (nothing stored).</summary>
public sealed class DashboardService(
    IJobOpeningRepository vacancies,
    ICompanyRepository companies,
    IVacancyInsights insights,
    StaffAccess access,
    ICandidateSideStore activity,
    TimeProvider clock)
{
    /// <summary>Same cap as the vacancies list page.</summary>
    public const int MaxVacancies = 100;

    public async Task<Result<DashboardData>> GetDashboardAsync(Actor actor, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<DashboardData>.Failure(member.Error!);
        }

        var companyId = member.Value!.CompanyId;
        var company = await companies.FindByIdAsync(companyId, ct);
        var open = await vacancies.ListByCompanyAsync(companyId, JobOpeningStatus.Open, null, 0, MaxVacancies, ct);
        var facts = await insights.ListApplicationFactsAsync(companyId, open.Select(vacancy => vacancy.Id).ToList(), ct);
        var now = clock.GetUtcNow();

        return Result<DashboardData>.Success(new DashboardData(
            company?.Name ?? string.Empty,
            DashboardBuilder.Vacancies(open, facts, now),
            DashboardBuilder.TopProfiles(open, facts),
            DashboardBuilder.Pending(open, facts),
            await activity.ListAiActivityAsync(companyId, 12, ct)));
    }

    public async Task<Result<IReadOnlyList<NavNotice>>> GetNoticesAsync(Actor actor, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actor.UserId, ct);
        if (!member.IsSuccess)
        {
            return Result<IReadOnlyList<NavNotice>>.Failure(member.Error!);
        }

        var companyId = member.Value!.CompanyId;
        IReadOnlyList<JobOpening> all = await vacancies.ListByCompanyAsync(companyId, null, null, 0, MaxVacancies, ct);
        var facts = await insights.ListApplicationFactsAsync(companyId, all.Select(vacancy => vacancy.Id).ToList(), ct);

        return Result<IReadOnlyList<NavNotice>>.Success(DashboardBuilder.Notices(all, facts, clock.GetUtcNow()));
    }
}
