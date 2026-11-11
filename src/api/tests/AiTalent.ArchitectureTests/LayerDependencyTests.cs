using NetArchTest.Rules;

namespace AiTalent.ArchitectureTests;

public class LayerDependencyTests
{
    private const string Application = "AiTalent.Application";
    private const string Infrastructure = "AiTalent.Infrastructure";
    private const string Api = "AiTalent.Api";

    [Fact]
    public void DomainDependsOnNothing()
    {
        var result = Types.InAssembly(typeof(AiTalent.Domain.Enums.EvaluationStatus).Assembly)
            .ShouldNot().HaveDependencyOnAny(Application, Infrastructure, Api)
            .GetResult();
        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void ApplicationDoesNotDependOnInfrastructureOrApi()
    {
        var result = Types.InAssembly(typeof(AiTalent.Application.DependencyInjection).Assembly)
            .ShouldNot().HaveDependencyOnAny(Infrastructure, Api)
            .GetResult();
        Assert.True(result.IsSuccessful);
    }
}
