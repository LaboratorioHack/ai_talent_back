using System.Net;

namespace AiTalent.IntegrationTests;

public class ProtectedEndpointTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/companies")]
    [InlineData("POST", "/api/companies/current/invite-codes")]
    [InlineData("POST", "/api/companies/join")]
    [InlineData("GET", "/api/staff")]
    [InlineData("PATCH", "/api/staff/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/vacancies")]
    [InlineData("POST", "/api/vacancies")]
    [InlineData("GET", "/api/vacancies/00000000-0000-0000-0000-000000000001")]
    [InlineData("PATCH", "/api/vacancies/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/vacancies/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/vacancies/00000000-0000-0000-0000-000000000001/publish")]
    [InlineData("POST", "/api/vacancies/00000000-0000-0000-0000-000000000001/close")]
    [InlineData("PUT", "/api/vacancies/00000000-0000-0000-0000-000000000001/profile")]
    [InlineData("GET", "/api/vacancies/00000000-0000-0000-0000-000000000001/applications")]
    [InlineData("POST", "/api/vacancies/00000000-0000-0000-0000-000000000001/applications/00000000-0000-0000-0000-000000000002/evaluate")]
    [InlineData("POST", "/api/vacancies/00000000-0000-0000-0000-000000000001/rank")]
    [InlineData("POST", "/api/vacancies/00000000-0000-0000-0000-000000000001/compare")]
    [InlineData("POST", "/api/vacancies/00000000-0000-0000-0000-000000000001/applications/00000000-0000-0000-0000-000000000002/evidence")]
    [InlineData("GET", "/api/dashboard")]
    [InlineData("GET", "/api/notifications")]
    public async Task EndpointsRejectRequestsWithoutToken(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
