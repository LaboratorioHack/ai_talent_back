using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Dashboard;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public class DashboardBuilderTests
{
    // 2026-11-12 15:00 in Bogotá.
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 20, 0, 0, TimeSpan.Zero);

    private static JobOpening Vacancy(string title = "Ingeniero Cloud", DateTimeOffset? closingAt = null) =>
        new(Guid.NewGuid(), null, title, null, closingAt, 1);

    [Fact]
    public void PeriodsCountByApplicationDate()
    {
        var vacancy = Vacancy(closingAt: new DateTimeOffset(2026, 11, 30, 3, 0, 0, TimeSpan.Zero));
        List<ApplicationFact> facts =
        [
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-2), fulfillment: "MEETS"),
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-20), status: "INTERVIEW", fulfillment: "PARTIALLY_MEETS"),
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-60), status: "HIRED", fulfillment: "DOES_NOT_MEET", disqualified: true),
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-3), resumeStatus: "FAILED"),
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-1), status: "WITHDRAWN"),
        ];

        var row = Assert.Single(DashboardBuilder.Vacancies([vacancy], facts, Now));

        Assert.Equal("2026-11-29", row.ClosesAt);
        Assert.Equal(new PeriodStats(2, 1, 1, 0, 0, 0, 0), row.ByPeriod["7d"]);
        Assert.Equal(new PeriodStats(3, 2, 1, 1, 0, 1, 0), row.ByPeriod["30d"]);
        Assert.Equal(new PeriodStats(4, 3, 1, 1, 1, 1, 1), row.ByPeriod["90d"]);
    }

    [Fact]
    public void VacancyWithoutClosingDateHasEmptyDate()
    {
        var row = Assert.Single(DashboardBuilder.Vacancies([Vacancy()], [], Now));

        Assert.Equal(string.Empty, row.ClosesAt);
        Assert.Equal(0, row.ByPeriod["90d"].Received);
    }

    [Fact]
    public void TopProfilesAreBestScoresAcrossVacancies()
    {
        var cloud = Vacancy("Cloud");
        var data = Vacancy("Datos");
        List<ApplicationFact> facts =
        [
            TestFacts.Fact(cloud.Id, fulfillment: "MEETS", score: 81.6m, name: "Ana"),
            TestFacts.Fact(data.Id, fulfillment: "MEETS", score: 92m, name: "Luis"),
            TestFacts.Fact(cloud.Id, fulfillment: "MEETS", score: 99m, status: "REJECTED", name: "Rejected"),
            TestFacts.Fact(cloud.Id, fulfillment: "DOES_NOT_MEET", score: 95m, disqualified: true, name: "Disqualified"),
            TestFacts.Fact(cloud.Id, name: "Not evaluated"),
        ];

        var top = DashboardBuilder.TopProfiles([cloud, data], facts);

        Assert.Equal(["Luis", "Ana"], top.Select(profile => profile.Name));
        Assert.Equal(82, top[1].Score);
        Assert.Equal("Datos", top[0].Vacancy);
    }

    [Fact]
    public void PendingFlagsUnreadableAndDuplicateCvs()
    {
        var vacancy = Vacancy("Analista");
        List<ApplicationFact> facts =
        [
            TestFacts.Fact(vacancy.Id, resumeStatus: "FAILED"),
            TestFacts.Fact(vacancy.Id, resumeStatus: "FAILED"),
            TestFacts.Fact(vacancy.Id, hash: "abc"),
            TestFacts.Fact(vacancy.Id, hash: "abc"),
            TestFacts.Fact(vacancy.Id, hash: "xyz"),
        ];

        var pending = DashboardBuilder.Pending([vacancy], facts);

        Assert.Collection(
            pending,
            item =>
            {
                Assert.Equal("unreadable", item.Kind);
                Assert.Equal("2 hojas de vida ilegibles en Analista", item.Text);
                Assert.Equal($"/candidates?job={vacancy.Id}", item.Href);
            },
            item =>
            {
                Assert.Equal("duplicate", item.Kind);
                Assert.Equal("1 hoja de vida duplicada en Analista", item.Text);
            });
    }

    [Fact]
    public void NoticesAreNewestFirstAndOnlyRecent()
    {
        var vacancy = Vacancy("Cloud");
        List<ApplicationFact> facts =
        [
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddMinutes(-20)),
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-30), fulfillment: "MEETS", evaluatedAt: Now.AddHours(-3)),
            TestFacts.Fact(vacancy.Id, appliedAt: Now.AddDays(-30), fulfillment: "MEETS", evaluatedAt: Now.AddDays(-10)),
        ];

        var notices = DashboardBuilder.Notices([vacancy], facts, Now);

        Assert.Collection(
            notices,
            notice =>
            {
                Assert.Equal("1 postulación nueva en Cloud", notice.Text);
                Assert.Equal("Hace 20 min", notice.When);
                Assert.Equal("success", notice.Tone);
            },
            notice =>
            {
                Assert.Equal("Cloud: 1 hoja de vida evaluada", notice.Text);
                Assert.Equal("Hace 3 h", notice.When);
                Assert.Equal($"/rankings/{vacancy.Id}", notice.Href);
            });
    }

    [Theory]
    [InlineData(0, "Ahora")]
    [InlineData(45, "Hace 45 min")]
    [InlineData(300, "Hace 5 h")]
    [InlineData(1000, "Ayer 22:20")]
    [InlineData(4320, "9 nov")]
    public void RelativeWhenUsesBogotaTime(int minutesAgo, string expected)
    {
        Assert.Equal(expected, DashboardBuilder.RelativeWhen(Now.AddMinutes(-minutesAgo), Now));
    }
}

public class DashboardServiceTests
{
    private readonly FakeStaffRepository _staff = new();
    private readonly FakeCompanyRepository _companies = new();
    private readonly FakeJobOpeningRepository _vacancies = new();
    private readonly FakeVacancyInsights _insights = new();

    private DashboardService CreateService() =>
        new(_vacancies, _companies, _insights, new StaffAccess(_staff), new FixedTimeProvider(DateTimeOffset.UtcNow));

    [Fact]
    public async Task DashboardShowsOnlyOpenVacanciesOfTheCompany()
    {
        var company = new Company("Acme", "Tecnología", "11-50");
        _companies.Add(company);
        var userId = Guid.NewGuid();
        _staff.Add(new StaffMember(userId, company.Id, "Ana", StaffRole.Recruiter));
        var open = new JobOpening(company.Id, userId, "Abierta", null, null, 1);
        open.Profiles.Add(new RoleProfile(open.Id, 1, null));
        open.Publish();
        var draft = new JobOpening(company.Id, userId, "Borrador", null, null, 1);
        var foreign = new JobOpening(Guid.NewGuid(), null, "Ajena", null, null, 1);
        foreign.Publish();
        _vacancies.Add(open);
        _vacancies.Add(draft);
        _vacancies.Add(foreign);
        _insights.Facts.Add(TestFacts.Fact(open.Id, fulfillment: "MEETS", score: 90m, name: "Luis"));

        var result = await CreateService().GetDashboardAsync(new Actor(userId, false));

        Assert.True(result.IsSuccess);
        Assert.Equal("Acme", result.Value!.Organization);
        Assert.Equal("Abierta", Assert.Single(result.Value.Vacancies).Title);
        Assert.Equal("Luis", Assert.Single(result.Value.TopProfiles).Name);
    }

    [Fact]
    public async Task NonMemberIsForbidden()
    {
        var dashboard = await CreateService().GetDashboardAsync(new Actor(Guid.NewGuid(), false));
        var notices = await CreateService().GetNoticesAsync(new Actor(Guid.NewGuid(), false));

        Assert.Equal(ErrorKind.Forbidden, dashboard.Error!.Kind);
        Assert.Equal(ErrorKind.Forbidden, notices.Error!.Kind);
    }
}
