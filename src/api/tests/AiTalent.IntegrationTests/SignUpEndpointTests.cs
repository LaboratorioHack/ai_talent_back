using System.Net;
using System.Net.Http.Json;

namespace AiTalent.IntegrationTests;

public class SignUpEndpointTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public async Task SignUpIsPublicAndValidatesInput()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/signup", UriKind.Relative),
            new { email = "not-an-email", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
