using System.Net;
using System.Text;
using AiTalent.Infrastructure.AiEngine;
using AiTalent.Application.Abstractions.AiEngine;
namespace AiTalent.UnitTests;

public class AiEngineClientTests
{
    [Fact]
    public async Task EvaluateAsync_UsesSnakeCasePayload()
    {
        var handler = new StubHandler(
            """
            {
              "evaluation_id": "11111111-1111-1111-1111-111111111111",
              "total_score": 88.5,
              "fulfillment": "MEETS",
              "confidence": 0.9,
              "recommend_interview": true,
              "disqualified": false,
              "strengths": [],
              "gaps": [],
              "requirement_assessments": [],
              "ai_model": "claude-test",
              "prompt_version": "eval-v1",
              "reasoning_summary": "ok"
            }
            """);
        var client = CreateClient(handler);

        var result = await client.EvaluateAsync(
            new EvaluateCandidateRequest(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333")));

        Assert.Equal(88.5f, result.TotalScore);
        Assert.Equal("MEETS", result.Fulfillment);
        Assert.Contains("role_profile_id", handler.LastRequestBody);
    }

    private static AiEngineClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000/") };
        return new AiEngineClient(httpClient);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
