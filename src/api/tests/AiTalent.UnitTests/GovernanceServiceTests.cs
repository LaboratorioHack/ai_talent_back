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

    [Fact]
    public async Task GetAudit_names_criba_ai_in_spanish_with_the_evaluation_result()
    {
        var company = new Company("Cordillera Financiera", "Finanzas", "51-200");
        _companies.Add(company);
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, company.Id, "Ana Martínez", StaffRole.Admin));
        _evaluations.GovernanceStats = new(1, 100, 0);
        _audit.Entries.Add(new(
            1,
            DateTimeOffset.UtcNow,
            null,
            "engine",
            "evaluation.completed",
            "evaluation",
            Guid.NewGuid().ToString(),
            """{"total_score": 81.9, "fulfillment": "MEETS"}""",
            null));
        _audit.Entries.Add(new(
            2,
            DateTimeOffset.UtcNow,
            userId,
            "user",
            "ranking.generated",
            "job_opening",
            Guid.NewGuid().ToString(),
            "{}",
            "Ana Martínez"));

        var decisions = (await CreateService().GetAuditAsync(userId, 30, default)).Value!.Decisions;

        Assert.Contains(decisions, d => d is { Who: GovernanceService.CribaAi, Action: "Evaluó una hoja de vida", Status: "meets" });
        Assert.Contains(decisions, d => d is { Who: "Ana M.", Action: "Pidió el ranking", Status: "none" });
    }

    [Fact]
    public void FormatWhen_uses_the_bogota_clock()
    {
        // 03:30 UTC on the 14th is 22:30 on the 13th in Bogotá: "today" there, not tomorrow.
        var now = new DateTimeOffset(2026, 11, 13, 23, 0, 0, TimeSpan.FromHours(-5));
        var occurred = new DateTimeOffset(2026, 11, 14, 3, 30, 0, TimeSpan.Zero);

        Assert.Equal("Hoy 22:30", GovernanceService.FormatWhen(occurred, now));
    }
}
