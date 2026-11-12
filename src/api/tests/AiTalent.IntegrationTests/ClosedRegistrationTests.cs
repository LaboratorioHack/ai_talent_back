using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AiTalent.IntegrationTests;

public class ClosedRegistrationTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public void SignUpEndpointIsNotMapped()
    {
        var endpoints = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints);

        Assert.DoesNotContain(endpoints, endpoint =>
            endpoint is RouteEndpoint route &&
            route.RoutePattern.RawText?.Contains("api/auth/signup", StringComparison.OrdinalIgnoreCase) == true);
    }
}
