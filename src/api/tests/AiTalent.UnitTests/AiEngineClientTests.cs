using System.Net;
using System.Text;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Infrastructure.AiEngine;

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
              "reasoning_summary": "ok",
              "fast_pass": {
                "used": true,
                "skipped_claude": false,
                "model": "typesafe/jev-1.14",
                "certainty": 0.82
              }
            }
            """);
        var client = CreateClient(handler);

        var result = await client.EvaluateAsync(
            new EvaluateCandidateRequest(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333")));

        Assert.Equal(88.5f, result.TotalScore);
        Assert.Equal("MEETS", result.Fulfillment);
        Assert.NotNull(result.FastPass);
        Assert.True(result.FastPass!.Used);
        Assert.False(result.FastPass.SkippedClaude);
        Assert.Equal(0.82f, result.FastPass.Certainty);
        Assert.Contains("role_profile_id", handler.LastRequestBody);
    }

    [Fact]
    public async Task IngestCvAsync_FromStorage_CallsIngestJsonEndpoint()
    {
        var handler = new StubHandler(
            """
            {
              "resume_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "extractor_version": "v1.1.0",
              "parser_strategy": "native_layout",
              "ocr_confidence": null,
              "chunks_count": 3,
              "professional_data": null
            }
            """);
        var client = CreateClient(handler);

        var result = await client.IngestCvAsync(
            new IngestCvRequest(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));

        Assert.Equal(3, result.ChunksCount);
        Assert.Contains("/v1/cvs/ingest-json", handler.LastRequestUri ?? "");
    }

    [Fact]
    public async Task IngestCvAsync_DeserializesBiasGuardrailTelemetry()
    {
        var handler = new StubHandler(
            """
            {
              "resume_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "extractor_version": "v1.1.0",
              "parser_strategy": "native_layout",
              "ocr_confidence": null,
              "chunks_count": 3,
              "professional_data": {
                "summary": "Backend engineer",
                "total_experience_months": 48,
                "seniority_level": "senior",
                "skills": ["python"],
                "parser_strategy": "native_layout",
                "ocr_confidence": null,
                "extractor_version": "v1.1.0",
                "bias_guardrail_telemetry": {
                  "pii_scrubbed": true,
                  "attributes_sanitized": {
                    "names_removed": 1,
                    "contact_info_removed": 2,
                    "gender_indicators_detected": 0,
                    "location_indicators_removed": 1,
                    "age_or_dob_indicators_removed": 0
                  },
                  "compliance_flags": ["residual_pii_detected"]
                }
              }
            }
            """);
        var client = CreateClient(handler);

        var result = await client.IngestCvAsync(
            new IngestCvRequest(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));

        Assert.NotNull(result.ProfessionalData);
        var telemetry = result.ProfessionalData!.BiasGuardrailTelemetry;
        Assert.NotNull(telemetry);
        Assert.True(telemetry!.PiiScrubbed);
        Assert.Equal(1, telemetry.AttributesSanitized.NamesRemoved);
        Assert.Equal(2, telemetry.AttributesSanitized.ContactInfoRemoved);
        Assert.Contains("residual_pii_detected", telemetry.ComplianceFlags);
    }

    [Fact]
    public async Task SearchEvidenceAsync_DeserializesSnakeCaseResponse()
    {
        var handler = new StubHandler(
            """
            {
              "resume_id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
              "query": "aws",
              "chunks": [
                {
                  "fragment_id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                  "section": "experience",
                  "citation": "Led AWS migration",
                  "page_number": 1,
                  "similarity": 0.92
                }
              ]
            }
            """);
        var client = CreateClient(handler);

        var result = await client.SearchEvidenceAsync(
            new EvidenceSearchRequest(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "aws"));

        Assert.Single(result.Chunks);
        Assert.Equal(0.92f, result.Chunks[0].Similarity);
    }

    [Fact]
    public async Task CompareAsync_DeserializesSnakeCaseResponse()
    {
        var handler = new StubHandler(
            """
            {
              "role_profile_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "comparative_summary": "Two strong backends.",
              "side_by_side_matrix": [
                {
                  "candidate_id": "11111111-1111-1111-1111-111111111111",
                  "primary_advantage": "Deeper Go",
                  "primary_trade_off_or_gap": "Less Kubernetes",
                  "ideal_team_fit": "Choose for backend depth"
                }
              ],
              "head_to_head_trade_off": "A vs B",
              "recommended_interview_focus": [
                {
                  "candidate_id": "11111111-1111-1111-1111-111111111111",
                  "target_gap_to_verify": "Kubernetes production",
                  "suggested_interview_question": "Walk through a cluster incident."
                }
              ]
            }
            """);
        var client = CreateClient(handler);

        var result = await client.CompareAsync(
            new CompareCandidatesRequest(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                [
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222")
                ]));

        Assert.Equal("Two strong backends.", result.ComparativeSummary);
        Assert.Single(result.SideBySideMatrix);
        Assert.Equal("Less Kubernetes", result.SideBySideMatrix[0].PrimaryTradeOffOrGap);
        Assert.Contains("resume_ids", handler.LastRequestBody);
        Assert.Contains("/v1/evaluations/compare", handler.LastRequestUri ?? "");
    }

    private static AiEngineClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000/") };
        return new AiEngineClient(httpClient);
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public string LastRequestBody { get; private set; } = "";
        public string? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
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
