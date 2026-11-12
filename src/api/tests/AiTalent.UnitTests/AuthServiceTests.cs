using AiTalent.Application.Common;
using AiTalent.Application.Features.Auth;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class AuthServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeCompanyRepository _companies = new();

    private AuthService CreateService() => new(_staff, _companies);

    [Fact]
    public async Task UserWithoutCompanyHasNoNameOrMembership()
    {
        var userId = Guid.NewGuid();

        var result = await CreateService().GetMeAsync(new Actor(userId, MfaVerified: false));

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.Null(result.Value.Name);
        Assert.Null(result.Value.Membership);
        Assert.False(result.Value.MfaVerified);
    }

    [Fact]
    public async Task ActiveMemberReturnsStaffNameCompanyAndRole()
    {
        var userId = Guid.NewGuid();
        var company = new Company("Acme", "Tecnología", "11-50");
        _companies.Add(company);
        _staff.Add(new StaffMember(userId, company.Id, "Jane Doe", StaffRole.Admin));

        var result = await CreateService().GetMeAsync(new Actor(userId, MfaVerified: true));

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane Doe", result.Value!.Name);
        Assert.True(result.Value.MfaVerified);
        Assert.Equal(company.Id, result.Value.Membership!.CompanyId);
        Assert.Equal("Acme", result.Value.Membership.CompanyName);
        Assert.Equal(StaffRole.Admin, result.Value.Membership.Role);
    }

    [Fact]
    public async Task DeactivatedMemberIsForbidden()
    {
        var userId = Guid.NewGuid();
        var company = new Company("Acme", "Tecnología", "11-50");
        _companies.Add(company);
        var member = new StaffMember(userId, company.Id, "Jane Doe", StaffRole.Recruiter);
        member.SetActive(false);
        _staff.Add(member);

        var result = await CreateService().GetMeAsync(new Actor(userId, MfaVerified: false));

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
    }

    [Fact]
    public async Task MembershipPointingAtMissingCompanyIsNotFound()
    {
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, Guid.NewGuid(), "Jane Doe", StaffRole.Admin));

        var result = await CreateService().GetMeAsync(new Actor(userId, MfaVerified: false));

        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }
}
