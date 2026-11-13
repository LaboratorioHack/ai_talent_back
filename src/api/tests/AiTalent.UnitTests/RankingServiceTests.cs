using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Rankings;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class RankingServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeEvaluationReadStore _evaluations = new();
    private readonly Guid _companyId = Guid.NewGuid();

    private RankingService CreateService() =>
        new(_vacancies, _evaluations, new StaffAccess(_staff));

    private Actor AddMember()
    {
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, _companyId, "Ana Martínez", StaffRole.Recruiter));
        return new Actor(userId, false);
    }

    [Fact]
    public async Task GetByVacancy_orders_stored_evaluations_without_reevaluating()
    {
        var actor = AddMember();
        var vacancy = new JobOpening(_companyId, actor.UserId, "Cloud Senior", "Tecnología", null, 1);
        var profile = new RoleProfile(vacancy.Id, 1, "Perfil cloud");
        profile.ReplaceRequirements([new Requirement(profile.Id, RequirementType.Mandatory, "SKILL", "Azure", null, 100, null)]);
        vacancy.Profiles.Add(profile);
        vacancy.Publish();
        _vacancies.Add(vacancy);

        var resumeLow = Guid.NewGuid();
        var resumeHigh = Guid.NewGuid();
        _evaluations.ByVacancy[(vacancy.Id, profile.Id)] =
        [
            Snapshot(resumeLow, "Baja", 55),
            Snapshot(resumeHigh, "Alta", 92),
        ];

        var result = await CreateService().GetByVacancyAsync(actor.UserId, vacancy.Id, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Candidates.Count);
        Assert.Equal("Alta", result.Value.Candidates[0].Name);
    }

    [Fact]
    public async Task GetByVacancy_returns_not_found_for_other_company()
    {
        var actor = AddMember();
        var other = new JobOpening(Guid.NewGuid(), null, "Other", null, null, 1);
        _vacancies.Add(other);

        var result = await CreateService().GetByVacancyAsync(actor.UserId, other.Id, default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    private static VacancyEvaluationSnapshot Snapshot(Guid resumeId, string name, decimal score) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            "CANDIDATE",
            resumeId,
            Guid.NewGuid(),
            score,
            "MEETS",
            false,
            0.85m,
            DateTimeOffset.UtcNow,
            null,
            []);
}
