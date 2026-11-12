using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Application.Features.Vacancies;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class VacancyServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeAuditLog _audit = new();
    private readonly Guid _companyId = Guid.NewGuid();

    private VacancyService CreateService() =>
        new(_vacancies, new StaffAccess(_staff), _audit, new FakeUnitOfWork());

    private Actor AddMember(Guid? companyId = null, StaffRole role = StaffRole.Recruiter, bool active = true)
    {
        var userId = Guid.NewGuid();
        var member = new StaffMember(userId, companyId ?? _companyId, "Test User", role);
        if (!active)
        {
            member.SetActive(false);
        }

        _staff.Add(member);
        return new Actor(userId, MfaVerified: false);
    }

    private static ProfileInput ValidProfile(decimal weight = 100m) =>
        new(
            "Diseñar y operar infraestructura cloud.",
            [
                new RequirementInput(
                    RequirementType.Mandatory,
                    "SKILL",
                    "Experiencia con Azure",
                    ">= 3 años",
                    weight,
                    new Dictionary<string, int> { ["AWS"] = 80 }),
            ]);

    private async Task<VacancyDetail> CreateVacancyAsync(Actor actor, string title = "Ingeniero Cloud", ProfileInput? profile = null)
    {
        var result = await CreateService().CreateAsync(
            actor,
            title,
            "Tecnología",
            DateTimeOffset.UtcNow.AddDays(10),
            2,
            profile ?? ValidProfile());

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task CreatingVacancyStoresDraftForCompanyAndCreator()
    {
        var actor = AddMember();

        var detail = await CreateVacancyAsync(actor);

        var vacancy = detail.Vacancy;
        Assert.Equal(JobOpeningStatus.Draft, vacancy.Status);
        Assert.Equal(_companyId, vacancy.CompanyId);
        Assert.Equal(actor.UserId, vacancy.CreatorId);
        Assert.Equal("Tecnología", vacancy.Area);
        Assert.Equal(2, vacancy.Positions);
        Assert.False(detail.ProfileInUse);

        var profile = vacancy.CurrentProfile;
        Assert.NotNull(profile);
        Assert.Equal(1, profile.Version);
        Assert.Equal(100m, profile.WeightTotal);
        var requirement = Assert.Single(profile.Requirements);
        Assert.Equal(RequirementType.Mandatory, requirement.Type);
        Assert.Equal(80, requirement.Equivalences["AWS"]);
        Assert.Contains("vacancy.created", _audit.Actions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingTitleIsRejected(string? title)
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            title,
            null,
            null,
            1,
            ValidProfile());

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task TooLongTitleIsRejected()
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            new string('x', 121),
            null,
            null,
            1,
            ValidProfile());

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task ShortAreaIsRejected()
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            "Ingeniero Cloud",
            "x",
            null,
            1,
            ValidProfile());

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task TooLongAreaIsRejected()
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            "Ingeniero Cloud",
            new string('x', 81),
            null,
            1,
            ValidProfile());

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task BlankAreaIsStoredAsNull()
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            "Ingeniero Cloud",
            "   ",
            null,
            1,
            ValidProfile());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Vacancy.Area);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task InvalidPositionsAreRejected(int positions)
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            "Ingeniero Cloud",
            null,
            null,
            positions,
            ValidProfile());

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task PastClosingDateIsRejected()
    {
        var result = await CreateService().CreateAsync(
            AddMember(),
            "Ingeniero Cloud",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            1,
            ValidProfile());

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task BlankRequirementDescriptionIsRejected()
    {
        var profile = new ProfileInput(
            null,
            [new RequirementInput(RequirementType.Mandatory, null, "  ", null, 100m, null)]);

        var result = await CreateService().CreateAsync(AddMember(), "Ingeniero Cloud", null, null, 1, profile);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task MissingRequirementWeightIsRejected()
    {
        var profile = new ProfileInput(
            null,
            [new RequirementInput(RequirementType.Mandatory, null, "Azure", null, null, null)]);

        var result = await CreateService().CreateAsync(AddMember(), "Ingeniero Cloud", null, null, 1, profile);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task InvalidRequirementWeightIsRejected(int weight)
    {
        var profile = new ProfileInput(
            null,
            [new RequirementInput(RequirementType.Mandatory, null, "Azure", null, weight, null)]);

        var result = await CreateService().CreateAsync(AddMember(), "Ingeniero Cloud", null, null, 1, profile);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidEquivalencePercentIsRejected(int percent)
    {
        var profile = new ProfileInput(
            null,
            [
                new RequirementInput(
                    RequirementType.Mandatory,
                    null,
                    "Azure",
                    null,
                    100m,
                    new Dictionary<string, int> { ["AWS"] = percent }),
            ]);

        var result = await CreateService().CreateAsync(AddMember(), "Ingeniero Cloud", null, null, 1, profile);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task RepeatedEquivalenceNamesAreRejected()
    {
        var profile = new ProfileInput(
            null,
            [
                new RequirementInput(
                    RequirementType.Mandatory,
                    null,
                    "Azure",
                    null,
                    100m,
                    new Dictionary<string, int> { ["AWS"] = 80, ["aws"] = 70 }),
            ]);

        var result = await CreateService().CreateAsync(AddMember(), "Ingeniero Cloud", null, null, 1, profile);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_vacancies.Vacancies);
    }

    [Fact]
    public async Task PublishingWithoutRequirementsIsRejected()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor, profile: new ProfileInput("Sin requisitos", []));

        var result = await CreateService().PublishAsync(actor.UserId, detail.Vacancy.Id);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task PublishingWithWrongWeightTotalIsRejected()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor, profile: ValidProfile(weight: 60m));

        var result = await CreateService().PublishAsync(actor.UserId, detail.Vacancy.Id);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Equal(JobOpeningStatus.Draft, detail.Vacancy.Status);
    }

    [Fact]
    public async Task PublishingWithWeightTotal100OpensVacancy()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor);

        var result = await CreateService().PublishAsync(actor.UserId, detail.Vacancy.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobOpeningStatus.Open, result.Value!.Vacancy.Status);
        Assert.Contains("vacancy.published", _audit.Actions);
    }

    [Fact]
    public async Task OnlyDraftsCanBePublished()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor);
        var service = CreateService();
        await service.PublishAsync(actor.UserId, detail.Vacancy.Id);

        var result = await service.PublishAsync(actor.UserId, detail.Vacancy.Id);

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
    }

    [Fact]
    public async Task OnlyOpenVacanciesCanBeClosed()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor);
        var service = CreateService();

        var draftClose = await service.CloseAsync(actor.UserId, detail.Vacancy.Id);
        Assert.Equal(ErrorKind.Invalid, draftClose.Error!.Kind);

        await service.PublishAsync(actor.UserId, detail.Vacancy.Id);
        var closed = await service.CloseAsync(actor.UserId, detail.Vacancy.Id);

        Assert.True(closed.IsSuccess);
        Assert.Equal(JobOpeningStatus.Closed, closed.Value!.Vacancy.Status);
        Assert.Contains("vacancy.closed", _audit.Actions);
    }

    [Fact]
    public async Task ProfileIsUpdatedInPlaceBeforeUse()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor);
        var profile = detail.Vacancy.CurrentProfile!;

        var updated = await CreateService().UpdateProfileAsync(
            actor.UserId,
            detail.Vacancy.Id,
            new ProfileInput(
                "Perfil actualizado",
                [
                    new RequirementInput(RequirementType.Mandatory, null, "Azure", null, 60m, null),
                    new RequirementInput(RequirementType.Desirable, null, "Inglés", null, 40m, null),
                ]));

        Assert.True(updated.IsSuccess);
        Assert.Single(detail.Vacancy.Profiles);
        Assert.Equal(1, profile.Version);
        Assert.Equal("Perfil actualizado", profile.Description);
        Assert.Equal(2, profile.Requirements.Count);
        Assert.Equal(100m, profile.WeightTotal);
        Assert.False(updated.Value!.ProfileInUse);
        Assert.Contains("vacancy.profile.updated", _audit.Actions);
    }

    [Fact]
    public async Task ProfileUsedByEvaluationGetsNewVersion()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor);
        var original = detail.Vacancy.CurrentProfile!;
        _vacancies.UsedProfiles.Add(original.Id);

        var updated = await CreateService().UpdateProfileAsync(
            actor.UserId,
            detail.Vacancy.Id,
            new ProfileInput("Nueva versión", ValidProfile().Requirements));

        Assert.True(updated.IsSuccess);
        Assert.Equal(2, detail.Vacancy.Profiles.Count);
        Assert.Equal(2, detail.Vacancy.CurrentProfile!.Version);
        Assert.Equal("Nueva versión", detail.Vacancy.CurrentProfile.Description);
        Assert.Equal(1, original.Version);
        Assert.Equal("Diseñar y operar infraestructura cloud.", original.Description);
        Assert.False(updated.Value!.ProfileInUse);
        Assert.Contains("vacancy.profile.versioned", _audit.Actions);
    }

    [Fact]
    public async Task ProfileCanBeAddedToVacancyWithoutOne()
    {
        var actor = AddMember();
        var created = await CreateService().CreateAsync(
            actor,
            "Ingeniero Cloud",
            null,
            null,
            1,
            profile: null);
        Assert.True(created.IsSuccess);
        Assert.Null(created.Value!.Vacancy.CurrentProfile);

        var updated = await CreateService().UpdateProfileAsync(
            actor.UserId,
            created.Value.Vacancy.Id,
            ValidProfile());

        Assert.True(updated.IsSuccess);
        Assert.Equal(1, updated.Value!.Vacancy.CurrentProfile!.Version);
        Assert.Contains("vacancy.profile.created", _audit.Actions);
    }

    [Fact]
    public async Task DeletedVacancyIsHiddenAndGone()
    {
        var actor = AddMember();
        var detail = await CreateVacancyAsync(actor);
        var service = CreateService();

        var deleted = await service.DeleteAsync(actor.UserId, detail.Vacancy.Id);
        var fetched = await service.GetAsync(actor.UserId, detail.Vacancy.Id);
        var page = await service.ListAsync(actor, null, null, null, null);

        Assert.True(deleted.IsSuccess);
        Assert.NotNull(detail.Vacancy.DeletedAt);
        Assert.Equal(ErrorKind.NotFound, fetched.Error!.Kind);
        Assert.Empty(page.Value!.Items);
        Assert.Contains("vacancy.deleted", _audit.Actions);
    }

    [Fact]
    public async Task OtherCompanyCannotSeeOrChangeVacancy()
    {
        var owner = AddMember();
        var other = AddMember(companyId: Guid.NewGuid());
        var detail = await CreateVacancyAsync(owner);
        var service = CreateService();
        var id = detail.Vacancy.Id;

        Assert.Equal(ErrorKind.NotFound, (await service.GetAsync(other.UserId, id)).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound,
            (await service.UpdateAsync(other, id, "Hijacked", null, null, 1)).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, (await service.PublishAsync(other.UserId, id)).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, (await service.CloseAsync(other.UserId, id)).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound, (await service.DeleteAsync(other.UserId, id)).Error!.Kind);
        Assert.Equal(ErrorKind.NotFound,
            (await service.UpdateProfileAsync(other.UserId, id, ValidProfile())).Error!.Kind);
    }

    [Fact]
    public async Task UserWithoutCompanyIsForbidden()
    {
        var stranger = new Actor(Guid.NewGuid(), MfaVerified: false);

        var list = await CreateService().ListAsync(stranger, null, null, null, null);
        var create = await CreateService().CreateAsync(stranger, "Vacante", null, null, 1, null);

        Assert.Equal(ErrorKind.Forbidden, list.Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, create.Error!.Kind);
    }

    [Fact]
    public async Task InactiveMemberIsForbidden()
    {
        var actor = AddMember(active: false);

        var result = await CreateService().ListAsync(actor, null, null, null, null);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task ListFiltersByStatusSearchAndPage()
    {
        var actor = AddMember();
        var service = CreateService();
        var cloud = await CreateVacancyAsync(actor, "Ingeniero Cloud");
        await CreateVacancyAsync(actor, "Analista de Datos");
        await service.PublishAsync(actor.UserId, cloud.Vacancy.Id);

        var all = await service.ListAsync(actor, null, null, 1, 20);
        var open = await service.ListAsync(actor, JobOpeningStatus.Open, null, 1, 20);
        var search = await service.ListAsync(actor, null, "datos", 1, 20);
        var firstPage = await service.ListAsync(actor, null, null, 1, 1);

        Assert.Equal(2, all.Value!.Total);
        Assert.Equal(1, open.Value!.Total);
        Assert.Equal("Ingeniero Cloud", open.Value.Items.Single().Title);
        Assert.Equal(1, search.Value!.Total);
        Assert.Equal("Analista de Datos", search.Value.Items.Single().Title);
        Assert.Single(firstPage.Value!.Items);
        Assert.Equal(2, firstPage.Value.Total);
    }
}
