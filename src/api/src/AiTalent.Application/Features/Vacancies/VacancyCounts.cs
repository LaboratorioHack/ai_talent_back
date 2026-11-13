using AiTalent.Application.Abstractions;

namespace AiTalent.Application.Features.Vacancies;

/// <summary>
/// Real counts per vacancy. Withdrawn applications are left out. A disqualified evaluation is
/// counted only as disqualified, never also as meets / partially meets / does not meet.
/// </summary>
public sealed record VacancyCounts(
    int Resumes,
    int Meets,
    int PartiallyMeets,
    int DoesNotMeet,
    int Disqualified,
    int Interviews)
{
    public static VacancyCounts Zero { get; } = new(0, 0, 0, 0, 0, 0);

    public static VacancyCounts From(IEnumerable<ApplicationFact> facts)
    {
        var active = facts.Where(fact => !IsStatus(fact.SelectionStatus, "WITHDRAWN")).ToList();
        var notDisqualified = active.Where(fact => !fact.Disqualified).ToList();

        return new VacancyCounts(
            active.Count,
            notDisqualified.Count(fact => IsStatus(fact.Fulfillment, "MEETS")),
            notDisqualified.Count(fact => IsStatus(fact.Fulfillment, "PARTIALLY_MEETS")),
            notDisqualified.Count(fact => IsStatus(fact.Fulfillment, "DOES_NOT_MEET")),
            active.Count(fact => fact.Disqualified),
            active.Count(fact => IsStatus(fact.SelectionStatus, "INTERVIEW")));
    }

    public static IReadOnlyDictionary<Guid, VacancyCounts> ByVacancy(IEnumerable<ApplicationFact> facts) =>
        facts.GroupBy(fact => fact.JobOpeningId).ToDictionary(group => group.Key, From);

    private static bool IsStatus(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
