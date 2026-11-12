using AiTalent.Application.Common;
using AiTalent.Application.Features.Vacancies;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class PublicVacancyServiceTests
{
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeCompanyRepository _companies = new();
    private readonly Guid _companyId = Guid.NewGuid();

    private PublicVacancyService CreateService() => new(_vacancies, _companies);

    private JobOpening CreateVacancy(DateTimeOffset? closingAt = null)
    {
        var company = new Company("Acme", "Tecnología", "11-50");
        typeof(Company).GetProperty(nameof(Company.Id))!.SetValue(company, _companyId);
        _companies.Add(company);

        var vacancy = new JobOpening(_companyId, null, "Ingeniero Cloud", "Tecnología", closingAt, 2);
        var profile = new RoleProfile(vacancy.Id, 1, "Diseñar infraestructura cloud.");
        profile.ReplaceRequirements(
        [
            new Requirement(profile.Id, RequirementType.Mandatory, "SKILL", "Experiencia con Azure", ">= 3 años", 70m, new Dictionary<string, int> { ["AWS"] = 80 }),
            new Requirement(profile.Id, RequirementType.Desirable, "LANGUAGE", "Inglés avanzado", null, 30m, null),
        ]);
        vacancy.Profiles.Add(profile);
        _vacancies.Add(vacancy);
        return vacancy;
    }

    [Fact]
    public async Task OpenVacancyReturnsPublicViewWithCompanyAndRequirements()
    {
        var vacancy = CreateVacancy(DateTimeOffset.UtcNow.AddDays(7));
        vacancy.Publish();

        var result = await CreateService().GetAsync(vacancy.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme", result.Value!.CompanyName);
        Assert.Equal(vacancy.Id, result.Value.Vacancy.Id);
        Assert.Equal("Diseñar infraestructura cloud.", result.Value.Vacancy.CurrentProfile!.Description);
        Assert.Equal(2, result.Value.Vacancy.CurrentProfile.Requirements.Count);
    }

    [Fact]
    public async Task OpenVacancyWithFutureClosingDateIsVisible()
    {
        var vacancy = CreateVacancy(DateTimeOffset.UtcNow.AddDays(1));
        vacancy.Publish();

        var result = await CreateService().GetAsync(vacancy.Id);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DraftVacancyIsNotFound()
    {
        var vacancy = CreateVacancy();

        var result = await CreateService().GetAsync(vacancy.Id);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task ClosedVacancyIsNotFound()
    {
        var vacancy = CreateVacancy();
        vacancy.Publish();
        vacancy.Close();

        var result = await CreateService().GetAsync(vacancy.Id);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task VacancyPastClosingDateIsNotFound()
    {
        var vacancy = CreateVacancy(DateTimeOffset.UtcNow.AddMinutes(-5));
        vacancy.Publish();

        var result = await CreateService().GetAsync(vacancy.Id);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task DeletedVacancyIsNotFound()
    {
        var vacancy = CreateVacancy();
        vacancy.Publish();
        vacancy.Delete();

        var result = await CreateService().GetAsync(vacancy.Id);

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }

    [Fact]
    public async Task MissingVacancyIsNotFound()
    {
        var result = await CreateService().GetAsync(Guid.NewGuid());

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }
}
