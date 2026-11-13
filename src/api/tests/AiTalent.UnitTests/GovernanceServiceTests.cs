using AiTalent.Application.Features.Governance;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class GovernanceServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeCompanyRepository _companies = new();
    private readonly FakeEvaluationReadStore _evaluations = new();
    private readonly FakeGovernanceReadStore _audit = new();
    private GovernanceService CreateService() =>
        new(_companies, _evaluations, _audit, new StaffAccess(_staff), TimeProvider.System);

    [Fact]
    public async Task GetAudit_returns_company_name_and_entries()
    {
        var company = new Company("Cordillera Financiera", "Finanzas", "51-200");
        _companies.Add(company);
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, company.Id, "Ana Martínez", StaffRole.Admin));
        _evaluations.GovernanceStats = new(12, 88, 0);
        _audit.HumanDecisions = 3;
        _audit.AiErrorsFlagged = 1;
        _audit.Entries.Add(new(
            1,
            DateTimeOffset.UtcNow,
            userId,
            "user",
            "vacancy.published",
            "job_opening",
            Guid.NewGuid().ToString(),
            "{}",
            "Ana Martínez"));

        var result = await CreateService().GetAuditAsync(userId, 30, default);
        Assert.True(result.IsSuccess);
        Assert.Equal("Cordillera Financiera", result.Value!.Organization);
        Assert.Equal(12, result.Value.Stats.ResumesEvaluated);
        Assert.Single(result.Value.Decisions);
        Assert.Empty(result.Value.Alerts);
    }
}
