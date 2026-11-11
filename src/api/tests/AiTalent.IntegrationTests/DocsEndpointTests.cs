using System.Net;

namespace AiTalent.IntegrationTests;

public class DocsEndpointTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task DocsAreAvailableInDevelopment(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SpecDeclaresBearerSecurityAndMarksProtectedEndpoints()
    {
        using var client = factory.CreateClient();
        var spec = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Contains("\"bearer\"", spec);
        Assert.Contains("/api/companies/join", spec);
    }
}
