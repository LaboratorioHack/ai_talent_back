using AiTalent.Application.Abstractions;
using AiTalent.Application.Features.Vacancies;

namespace AiTalent.UnitTests;

internal static class TestFacts
{
    public static ApplicationFact Fact(
        Guid vacancyId,
        string status = "APPLIED",
        string? resumeStatus = "PROCESSED",
        string? fulfillment = null,
        bool disqualified = false,
        decimal? score = null,
        DateTimeOffset? appliedAt = null,
        DateTimeOffset? evaluatedAt = null,
        string? hash = null,
        string name = "Candidate") =>
        new(
            Guid.NewGuid(),
            vacancyId,
            name,
            status,
            appliedAt ?? DateTimeOffset.UtcNow,
            resumeStatus,
            hash,
            appliedAt ?? DateTimeOffset.UtcNow,
            fulfillment,
            disqualified,
            score,
            evaluatedAt ?? (fulfillment is null ? null : DateTimeOffset.UtcNow));
}

public class VacancyCountsTests
{
    [Fact]
    public void CountsBucketsByLatestEvaluationAndStage()
    {
        var vacancy = Guid.NewGuid();
        var counts = VacancyCounts.From(
        [
            TestFacts.Fact(vacancy, fulfillment: "MEETS"),
            TestFacts.Fact(vacancy, fulfillment: "PARTIALLY_MEETS"),
            TestFacts.Fact(vacancy, fulfillment: "DOES_NOT_MEET"),
            TestFacts.Fact(vacancy, fulfillment: "DOES_NOT_MEET", disqualified: true),
            TestFacts.Fact(vacancy, status: "INTERVIEW", fulfillment: "MEETS"),
            TestFacts.Fact(vacancy),
            TestFacts.Fact(vacancy, status: "WITHDRAWN", fulfillment: "MEETS"),
        ]);

        Assert.Equal(new VacancyCounts(6, 2, 1, 1, 1, 1), counts);
    }

    [Fact]
    public void NoApplicationsGivesZero()
    {
        Assert.Equal(VacancyCounts.Zero, VacancyCounts.From([]));
    }

    [Fact]
    public void ByVacancyGroupsFacts()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var counts = VacancyCounts.ByVacancy(
        [
            TestFacts.Fact(first),
            TestFacts.Fact(first),
            TestFacts.Fact(second, fulfillment: "MEETS"),
        ]);

        Assert.Equal(2, counts[first].Resumes);
        Assert.Equal(1, counts[second].Meets);
    }
}
