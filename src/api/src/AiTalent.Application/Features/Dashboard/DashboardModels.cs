namespace AiTalent.Application.Features.Dashboard;

public sealed record PeriodStats(
    int Received,
    int Read,
    int Meets,
    int Partial,
    int DoesNotMeet,
    int Interview,
    int Finalist);

public sealed record DashboardVacancy(
    Guid Id,
    string Title,
    string ClosesAt,
    IReadOnlyDictionary<string, PeriodStats> ByPeriod);

public sealed record TopProfile(Guid ApplicationId, string Name, string Vacancy, int Score);

/// <summary>`kind` is unreadable, duplicate or contrast (contrast needs interviews, not computed yet).</summary>
public sealed record PendingItem(string Id, string Kind, string Text, string ActionLabel, string Href);

public sealed record DashboardData(
    string Organization,
    IReadOnlyList<DashboardVacancy> Vacancies,
    IReadOnlyList<TopProfile> TopProfiles,
    IReadOnlyList<PendingItem> Pending);

/// <summary>Bell notice. `tone` is success, warning or danger; `when` is already formatted in Spanish (Bogotá time).</summary>
public sealed record NavNotice(
    string Id,
    string Text,
    string When,
    string Href,
    string Tone,
    DateTimeOffset OccurredAt);
