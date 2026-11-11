using AiTalent.Domain.Enums;

namespace AiTalent.UnitTests;

public class EvaluationStatusTests
{
    [Fact]
    public void HasThreeStates() => Assert.Equal(3, Enum.GetValues<EvaluationStatus>().Length);
}
