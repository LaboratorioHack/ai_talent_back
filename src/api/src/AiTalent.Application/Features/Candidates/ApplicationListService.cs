using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Application.Features.Candidates;

public sealed class ApplicationListService(
    IJobOpeningRepository vacancies,
    IApplicationRepository applications,
    StaffAccess access)
{
    public async Task<Result<IReadOnlyList<ApplicationSubmission>>> ListByVacancyAsync(
        Guid actorUserId,
        Guid vacancyId,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<IReadOnlyList<ApplicationSubmission>>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, vacancyId, ct);
        if (vacancy is null)
        {
            return Result<IReadOnlyList<ApplicationSubmission>>.Failure(
                ErrorKind.NotFound,
                "Vacancy not found.");
        }

        var submissions = await applications.ListByJobOpeningAsync(vacancyId, ct);
        return Result<IReadOnlyList<ApplicationSubmission>>.Success(submissions);
    }
}
