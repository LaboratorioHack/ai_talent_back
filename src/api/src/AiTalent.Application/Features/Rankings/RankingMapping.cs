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
        IReadOnlyList<RequirementResultResponse> results) =>
        Portal.ResumeDocBuilder.Build(candidateName, [], professionalDataJson);

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
