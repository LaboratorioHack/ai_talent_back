using AiTalent.Application.Common;
using AiTalent.Application.Features.Companies;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class CompanyServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeCompanyRepository _companies = new();
    private readonly FakeInviteCodes _codes = new();
    private readonly FakeAuditLog _audit = new();
    private readonly FakeApplicationRepository _applications = new();

    private CompanyService CreateService(bool duplicateKey = false) =>
        new(_companies, _staff, _applications, new StaffAccess(_staff), _codes, _audit, new FakeUnitOfWork(duplicateKey));

    [Fact]
    public async Task SimultaneousCreateFromSameUserReportsConflict()
    {
        var result = await CreateService(duplicateKey: true).CreateAsync(Guid.NewGuid(), "Acme", "Tecnología", "11-50", "Jane Doe");

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Empty(_audit.Actions);
    }

    [Fact]
    public async Task AnApplicantAccountCannotCreateOrJoinACompany()
    {
        var userId = Guid.NewGuid();
        var application = new AiTalent.Domain.Entities.Application(Guid.NewGuid(), Guid.NewGuid(), ApplicationSource.Candidate);
        application.MarkSubmittedBy(userId);
        _applications.Applications.Add(application);
        var service = CreateService();

        var created = await service.CreateAsync(userId, "Acme", "Tecnología", "11-50", "Jane Doe", "jane@example.com");
        var joined = await service.JoinAsync(userId, "any-code", "Jane Doe", "jane@example.com");

        Assert.Equal(ErrorKind.Forbidden, created.Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, joined.Error!.Kind);
        Assert.Empty(_staff.Members);
        Assert.Empty(_companies.Companies);
    }

    [Fact]
    public async Task ACvUploadedByARecruiterDoesNotMakeThePersonAnApplicantAccount()
    {
        var userId = Guid.NewGuid();
        _applications.Applications.Add(new AiTalent.Domain.Entities.Application(Guid.NewGuid(), Guid.NewGuid(), ApplicationSource.CompanyUpload));

        var created = await CreateService().CreateAsync(userId, "Acme", "Tecnología", "11-50", "Jane Doe", "jane@example.com");

        Assert.True(created.IsSuccess);
    }

    [Fact]
    public async Task CreatingCompanyMakesCallerItsAdmin()
    {
        var userId = Guid.NewGuid();

        var result = await CreateService().CreateAsync(userId, "Acme", "Tecnología", "11-50", "Jane Doe");

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffRole.Admin, result.Value!.Role);
        Assert.Single(_companies.Companies);
        Assert.Equal(result.Value.CompanyId, _staff.Members.Single().CompanyId);
    }

    [Fact]
    public async Task UserWhoAlreadyBelongsToCompanyCannotCreateAnother()
    {
        var userId = Guid.NewGuid();
        var service = CreateService();
        await service.CreateAsync(userId, "Acme", "Tecnología", "11-50", "Jane Doe");

        var result = await service.CreateAsync(userId, "Other", "Tecnología", "11-50", "Jane Doe");

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Single(_companies.Companies);
    }

    [Fact]
    public async Task ValidInviteCodeAddsRecruiterToCompany()
    {
        var admin = Guid.NewGuid();
        var service = CreateService();
        var created = await service.CreateAsync(admin, "Acme", "Tecnología", "11-50", "Jane Doe");
        var invite = await service.CreateInviteCodeAsync(new Actor(admin, MfaVerified: true));

        var joined = await service.JoinAsync(Guid.NewGuid(), invite.Value!.Code, "John Roe");

        Assert.True(joined.IsSuccess);
        Assert.Equal(created.Value!.CompanyId, joined.Value!.CompanyId);
        Assert.Equal(StaffRole.Recruiter, joined.Value.Role);
    }

    [Fact]
    public async Task InvalidInviteCodeIsRejectedWithGenericMessage()
    {
        var result = await CreateService().JoinAsync(Guid.NewGuid(), "not-a-real-code", "John Roe");

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Equal("The invite code is invalid or has expired.", result.Error.Message);
    }

    [Fact]
    public async Task RecruiterCannotCreateInviteCodes()
    {
        var admin = Guid.NewGuid();
        var recruiter = Guid.NewGuid();
        var service = CreateService();
        await service.CreateAsync(admin, "Acme", "Tecnología", "11-50", "Jane Doe");
        var invite = await service.CreateInviteCodeAsync(new Actor(admin, MfaVerified: true));
        await service.JoinAsync(recruiter, invite.Value!.Code, "John Roe");

        var result = await service.CreateInviteCodeAsync(new Actor(recruiter, MfaVerified: true));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task AdminWithoutMfaCannotCreateInviteCodes()
    {
        var admin = Guid.NewGuid();
        var service = CreateService();
        await service.CreateAsync(admin, "Acme", "Tecnología", "11-50", "Jane Doe");

        var result = await service.CreateInviteCodeAsync(new Actor(admin, MfaVerified: false));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task CreatingCompanyStoresSectorAndSize()
    {
        var userId = Guid.NewGuid();

        var result = await CreateService().CreateAsync(userId, " Acme ", " Tecnología ", " 11-50 ", "Jane Doe");

        Assert.True(result.IsSuccess);
        Assert.Equal("Tecnología", result.Value!.Sector);
        Assert.Equal("11-50", result.Value.Size);
        var company = _companies.Companies.Single();
        Assert.Equal("Acme", company.Name);
        Assert.Equal("Tecnología", company.Sector);
        Assert.Equal("11-50", company.Size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    public async Task InvalidSectorIsRejected(string? sector)
    {
        var result = await CreateService().CreateAsync(Guid.NewGuid(), "Acme", sector, "11-50", "Jane Doe");

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_companies.Companies);
        Assert.Empty(_staff.Members);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("11 a 50")]
    [InlineData("1001+")]
    public async Task InvalidSizeIsRejected(string? size)
    {
        var result = await CreateService().CreateAsync(Guid.NewGuid(), "Acme", "Tecnología", size, "Jane Doe");

        Assert.Equal(ErrorKind.Invalid, result.Error!.Kind);
        Assert.Empty(_companies.Companies);
        Assert.Empty(_staff.Members);
    }

    [Fact]
    public async Task DeactivatedStaffMemberIsDenied()
    {
        var admin = Guid.NewGuid();
        var service = CreateService();
        await service.CreateAsync(admin, "Acme", "Tecnología", "11-50", "Jane Doe");
        _staff.Members.Single().SetActive(false);

        var result = await service.CreateInviteCodeAsync(new Actor(admin, MfaVerified: true));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }
}
