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

    private CompanyService CreateService(bool duplicateKey = false) =>
        new(_companies, _staff, new StaffAccess(_staff), _codes, _audit, new FakeUnitOfWork(duplicateKey));

    [Fact]
    public async Task SimultaneousCreateFromSameUserReportsConflict()
    {
        var result = await CreateService(duplicateKey: true).CreateAsync(Guid.NewGuid(), "Acme", "Jane Doe");

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Empty(_audit.Actions);
    }

    [Fact]
    public async Task CreatingCompanyMakesCallerItsAdmin()
    {
        var userId = Guid.NewGuid();

        var result = await CreateService().CreateAsync(userId, "Acme", "Jane Doe");

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
        await service.CreateAsync(userId, "Acme", "Jane Doe");

        var result = await service.CreateAsync(userId, "Other", "Jane Doe");

        Assert.Equal(ErrorKind.Conflict, result.Error!.Kind);
        Assert.Single(_companies.Companies);
    }

    [Fact]
    public async Task ValidInviteCodeAddsRecruiterToCompany()
    {
        var admin = Guid.NewGuid();
        var service = CreateService();
        var created = await service.CreateAsync(admin, "Acme", "Jane Doe");
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
        await service.CreateAsync(admin, "Acme", "Jane Doe");
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
        await service.CreateAsync(admin, "Acme", "Jane Doe");

        var result = await service.CreateInviteCodeAsync(new Actor(admin, MfaVerified: false));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task DeactivatedStaffMemberIsDenied()
    {
        var admin = Guid.NewGuid();
        var service = CreateService();
        await service.CreateAsync(admin, "Acme", "Jane Doe");
        _staff.Members.Single().SetActive(false);

        var result = await service.CreateInviteCodeAsync(new Actor(admin, MfaVerified: true));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }
}
