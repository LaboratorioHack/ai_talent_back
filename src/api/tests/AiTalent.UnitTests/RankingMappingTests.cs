using AiTalent.Application.Features.Rankings;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class RankingMappingTests
{
    [Theory]
    [InlineData(25, 82, 20.5)]
    [InlineData(20, 100, 20)]
    [InlineData(15, 0, 0)]
    public void Contribution_IsTheWeightTimesHowWellItIsMet(decimal weight, decimal score, double expected)
    {
        var requirement = new Requirement(Guid.NewGuid(), RequirementType.Mandatory, null, "Kubernetes", null, weight, null);

        Assert.Equal(expected, RankingMapping.Contribution(score, requirement));
    }

    [Fact]
    public void Contribution_OfADisqualifyingRequirement_IsZero()
    {
        var requirement = new Requirement(Guid.NewGuid(), RequirementType.Disqualifying, null, "Sin permiso de trabajo", null, 0, null);

        Assert.Equal(0, RankingMapping.Contribution(100, requirement));
    }

    [Fact]
    public void ToIsoDate_UsesTheBogotaCalendarDay()
    {
        // 23:59 in Bogotá is already the next day in UTC.
        var closing = new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.FromHours(-5));

        Assert.Equal("2026-11-30", RankingMapping.ToIsoDate(closing));
    }
}
