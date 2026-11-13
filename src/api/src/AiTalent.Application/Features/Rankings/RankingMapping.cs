using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Rankings;

internal static class RankingMapping
{
    public static string ToFrontendFulfillment(string dbValue) =>
        dbValue switch
        {
            "PARTIALLY_MEETS" => "PARTIAL",
            "MEETS" => "MEETS",
            _ => "DOES_NOT_MEET",
        };

    public static string ToFrontendResult(string dbValue) =>
        dbValue switch
        {
            "PARTIALLY_MEETS" => "PARTIAL",
            "MEETS" => "MEETS",
            "DOES_NOT_MEET" => "DOES_NOT_MEET",
            _ => "NO_EVIDENCE",
        };

    public static string ToFrontendRequirementType(RequirementType type) =>
        type switch
        {
            RequirementType.Desirable => "NICE_TO_HAVE",
            RequirementType.Disqualifying => "DISQUALIFYING",
            _ => "MANDATORY",
        };

    public static string SourceLabel(string source) =>
        string.Equals(source, "CANDIDATE", StringComparison.OrdinalIgnoreCase)
            ? "Se postuló con el enlace"
            : "Subida por tu equipo";

    public static string? ToIsoDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd");

    public static RankedCandidateResponse MapCandidate(
        VacancyEvaluationSnapshot snapshot,
        IReadOnlyList<Requirement> requirements,
        int rankFromEngine)
    {
        var mandatory = requirements.Where(requirement => requirement.Type == RequirementType.Mandatory).ToList();
        var mandatoryMet = snapshot.Results.Count(result =>
        {
            var requirement = requirements.FirstOrDefault(req => req.Id == result.RequirementId);
            return requirement?.Type == RequirementType.Mandatory
                   && string.Equals(result.Result, "MEETS", StringComparison.OrdinalIgnoreCase);
        });

        var fulfillment = snapshot.Disqualified
            ? "DOES_NOT_MEET"
            : ToFrontendFulfillment(snapshot.Fulfillment);

        var results = snapshot.Results
            .Select(result =>
            {
                var requirement = requirements.FirstOrDefault(req => req.Id == result.RequirementId);
                return new RequirementResultResponse(
                    result.RequirementResultId,
                    result.RequirementId,
                    ToFrontendResult(result.Result),
                    (double)result.Score,
                    (double)result.Confidence,
                    result.Justification ?? string.Empty,
                    result.Evidence.Select(evidence => new EvidenceResponse(
                        evidence.EvidenceId,
                        evidence.Citation,
                        evidence.Location ?? string.Empty)).ToList());
            })
            .ToList();

        var toValidate = results
            .Where(result => result.Result is "PARTIAL")
            .Select(result =>
            {
                var description = requirements.FirstOrDefault(req => req.Id == result.RequirementId)?.Description ?? "Requisito";
                return new ToValidateResponse($"{description} parcial", "warning");
            })
            .Take(3)
            .ToList();

        return new RankedCandidateResponse(
            snapshot.ApplicationId,
            snapshot.CandidateName,
            (double)snapshot.TotalScore,
            fulfillment,
            mandatoryMet,
            mandatory.Count,
            (double)snapshot.Confidence,
            snapshot.EvaluatedAt.ToString("o"),
            SourceLabel(snapshot.ApplicationSource),
            results,
            toValidate,
            BuildResumeDoc(snapshot.CandidateName, snapshot.ProfessionalDataJson, requirements, results));
    }

    public static ResumeDocResponse BuildResumeDoc(
        string candidateName,
        string? professionalDataJson,
        IReadOnlyList<Requirement> requirements,
        IReadOnlyList<RequirementResultResponse> results)
    {
        var sections = new List<ResumeSectionResponse>();
        if (!string.IsNullOrWhiteSpace(professionalDataJson))
        {
            try
            {
                using var document = JsonDocument.Parse(professionalDataJson);
                var root = document.RootElement;
                var summary = root.TryGetProperty("summary", out var summaryNode) ? summaryNode.GetString() : null;
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    sections.Add(new ResumeSectionResponse("Resumen", [new ResumeLineResponse("text", summary, null, null)]));
                }

                if (root.TryGetProperty("skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
                {
                    var lines = skills.EnumerateArray()
                        .Select(skill => new ResumeLineResponse("bullet", skill.GetString() ?? string.Empty, null, null))
                        .ToList();
                    if (lines.Count > 0)
                    {
                        sections.Add(new ResumeSectionResponse("Habilidades", lines));
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to a minimal document below.
            }
        }

        if (sections.Count == 0)
        {
            sections.Add(new ResumeSectionResponse(
                "Experiencia",
                [
                    new ResumeLineResponse(
                        "text",
                        "La hoja de vida está disponible; Criba AI aún no generó una vista enriquecida.",
                        null,
                        null),
                ]));
        }

        return new ResumeDocResponse(candidateName, candidateName, [], sections, 1);
    }

    public static IReadOnlyList<RankRequirementResponse> MapRequirements(IReadOnlyList<Requirement> requirements) =>
        requirements
            .Select(requirement => new RankRequirementResponse(
                requirement.Id,
                requirement.Description,
                ToFrontendRequirementType(requirement.Type),
                requirement.Type == RequirementType.Disqualifying ? null : requirement.Weight,
                requirement.Category,
                requirement.Equivalences
                    .Select(pair => new EquivalenceResponse(pair.Key, pair.Value))
                    .ToList()))
            .ToList();
}
