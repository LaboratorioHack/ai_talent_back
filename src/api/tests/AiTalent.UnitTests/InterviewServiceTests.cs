using AiTalent.Application.Common;
using AiTalent.Application.Features.Interviews;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class InterviewServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeInterviewReadStore _interviews = new();
    private readonly Guid _companyId = Guid.NewGuid();

    private InterviewService CreateService() => new(_interviews, new StaffAccess(_staff));

    private Guid AddMember()
    {
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, _companyId, "Carlos Pérez", StaffRole.Recruiter));
        return userId;
    }

    [Fact]
    public async Task Get_maps_completed_status_to_done()
    {
        var userId = AddMember();
        var id = Guid.NewGuid();
        _interviews.Interviews.Add(new(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Julián Ríos",
            "julian@example.com",
            "Cloud Senior",
            "COMPLETED",
            DateTimeOffset.UtcNow.AddMinutes(-20),
            DateTimeOffset.UtcNow.AddMinutes(-25),
            DateTimeOffset.UtcNow,
            "ADVANCE",
            "[{\"label\":\"Inglés\",\"cvSays\":\"B2\",\"interviewSays\":\"Fluido\",\"verdict\":\"match\"}]",
            [new(1, "Inglés", "¿Hablas inglés?", "Sí", "match")]));

        var result = await CreateService().GetAsync(userId, id, default);
        Assert.True(result.IsSuccess);
        Assert.Equal("DONE", result.Value!.Status);
        Assert.Single(result.Value.Contrasts);
        Assert.Equal("ADVANCE", result.Value.Recommendation);
    }

    [Fact]
    public async Task Get_returns_not_found_when_missing()
    {
        var userId = AddMember();
        var result = await CreateService().GetAsync(userId, Guid.NewGuid(), default);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.NotFound, result.Error!.Kind);
    }
}
