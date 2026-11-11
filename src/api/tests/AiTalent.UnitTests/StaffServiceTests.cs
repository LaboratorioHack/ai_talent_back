using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class StaffServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Actor _admin = new(Guid.NewGuid(), MfaVerified: true);

    private StaffService CreateService() =>
        new(_staff, new StaffAccess(_staff), new FakeAuditLog(), new FakeUnitOfWork());

    public StaffServiceTests()
    {
        _staff.Add(new StaffMember(_admin.UserId, _companyId, "Ada Admin", StaffRole.Admin));
    }

    [Fact]
    public async Task AdminCannotDemoteAnotherAdmin()
    {
        var otherAdmin = Guid.NewGuid();
        _staff.Add(new StaffMember(otherAdmin, _companyId, "Ben Admin", StaffRole.Admin));

        var result = await CreateService().UpdateAsync(_admin, otherAdmin, StaffRole.Recruiter, null);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.Equal(StaffRole.Admin, _staff.Members.Single(m => m.UserId == otherAdmin).Role);
    }

    [Fact]
    public async Task AdminCannotDeactivateAnotherAdmin()
    {
        var otherAdmin = Guid.NewGuid();
        _staff.Add(new StaffMember(otherAdmin, _companyId, "Ben Admin", StaffRole.Admin));

        var result = await CreateService().UpdateAsync(_admin, otherAdmin, null, active: false);

        Assert.Equal(ErrorKind.Forbidden, result.Error!.Kind);
        Assert.True(_staff.Members.Single(m => m.UserId == otherAdmin).Active);
    }

    [Fact]
    public async Task AdminCanPromoteRecruiterToAdmin()
    {
        var recruiter = Guid.NewGuid();
        _staff.Add(new StaffMember(recruiter, _companyId, "Rita Recruiter", StaffRole.Recruiter));

        var result = await CreateService().UpdateAsync(_admin, recruiter, StaffRole.Admin, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffRole.Admin, result.Value!.Role);
    }
}
