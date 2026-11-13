using AiTalent.Application.Abstractions;
using AiTalent.Domain.Entities;

namespace AiTalent.Application.Features.Dashboard;

/// <summary>Pure calculations for the home dashboard and the bell, from application facts.</summary>
public static class DashboardBuilder
{
    /// <summary>The app is used in Colombia; Bogotá has no daylight saving time.</summary>
    public static readonly TimeSpan BogotaOffset = TimeSpan.FromHours(-5);

    public const int TopProfilesCount = 5;
    public const int MaxNotices = 10;
    public static readonly TimeSpan NoticeWindow = TimeSpan.FromDays(7);

    private static readonly (string Key, int Days)[] Periods = [("7d", 7), ("30d", 30), ("90d", 90)];

    private static readonly string[] Months =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    public static IReadOnlyList<DashboardVacancy> Vacancies(
        IReadOnlyList<JobOpening> openVacancies,
        IReadOnlyList<ApplicationFact> facts,
        DateTimeOffset now)
    {
        var byVacancy = facts.ToLookup(fact => fact.JobOpeningId);
        return openVacancies
            .Select(vacancy => new DashboardVacancy(
                vacancy.Id,
                vacancy.Title,
                LocalDate(vacancy.ClosingAt),
                Periods.ToDictionary(
                    period => period.Key,
                    period => Stats(byVacancy[vacancy.Id].Where(fact => fact.AppliedAt >= now.AddDays(-period.Days))))))
            .ToList();
    }

    public static PeriodStats Stats(IEnumerable<ApplicationFact> facts)
    {
        var active = facts.Where(fact => !Is(fact.SelectionStatus, "WITHDRAWN")).ToList();
        return new PeriodStats(
            active.Count,
            active.Count(fact => Is(fact.ResumeStatus, "PROCESSED")),
            active.Count(fact => !fact.Disqualified && Is(fact.Fulfillment, "MEETS")),
            active.Count(fact => !fact.Disqualified && Is(fact.Fulfillment, "PARTIALLY_MEETS")),
            active.Count(fact => fact.Disqualified || Is(fact.Fulfillment, "DOES_NOT_MEET")),
            active.Count(fact => Is(fact.SelectionStatus, "INTERVIEW")),
            active.Count(fact => Is(fact.SelectionStatus, "HIRED")));
    }

    public static IReadOnlyList<TopProfile> TopProfiles(
        IReadOnlyList<JobOpening> vacancies,
        IReadOnlyList<ApplicationFact> facts)
    {
        var titles = vacancies.ToDictionary(vacancy => vacancy.Id, vacancy => vacancy.Title);
        return facts
            .Where(fact => fact.TotalScore is not null
                && !fact.Disqualified
                && !Is(fact.SelectionStatus, "WITHDRAWN")
                && !Is(fact.SelectionStatus, "REJECTED")
                && titles.ContainsKey(fact.JobOpeningId))
            .OrderByDescending(fact => fact.TotalScore)
            .ThenBy(fact => fact.AppliedAt)
            .Take(TopProfilesCount)
            .Select(fact => new TopProfile(
                fact.ApplicationId,
                fact.CandidateName,
                titles[fact.JobOpeningId],
                (int)Math.Round(fact.TotalScore!.Value, MidpointRounding.AwayFromZero),
                fact.JobOpeningId))
            .ToList();
    }

    public static IReadOnlyList<PendingItem> Pending(
        IReadOnlyList<JobOpening> vacancies,
        IReadOnlyList<ApplicationFact> facts)
    {
        var byVacancy = facts.ToLookup(fact => fact.JobOpeningId);
        var items = new List<PendingItem>();
        foreach (var vacancy in vacancies)
        {
            var active = Active(byVacancy[vacancy.Id]);
            var unreadable = active.Count(fact => Is(fact.ResumeStatus, "FAILED"));
            if (unreadable > 0)
            {
                items.Add(new PendingItem(
                    $"unreadable-{vacancy.Id}",
                    "unreadable",
                    $"{Plural(unreadable, "hoja de vida ilegible", "hojas de vida ilegibles")} en {vacancy.Title}",
                    "Revisar",
                    CandidatesHref(vacancy.Id)));
            }

            var duplicates = Duplicates(active);
            if (duplicates > 0)
            {
                items.Add(new PendingItem(
                    $"duplicate-{vacancy.Id}",
                    "duplicate",
                    $"{Plural(duplicates, "hoja de vida duplicada", "hojas de vida duplicadas")} en {vacancy.Title}",
                    "Quitar",
                    CandidatesHref(vacancy.Id)));
            }
        }

        return items;
    }

    public static IReadOnlyList<NavNotice> Notices(
        IReadOnlyList<JobOpening> vacancies,
        IReadOnlyList<ApplicationFact> facts,
        DateTimeOffset now)
    {
        var since = now - NoticeWindow;
        var byVacancy = facts.ToLookup(fact => fact.JobOpeningId);
        var notices = new List<NavNotice>();
        foreach (var vacancy in vacancies)
        {
            var active = Active(byVacancy[vacancy.Id]);

            var failed = active.Where(fact => Is(fact.ResumeStatus, "FAILED")).ToList();
            if (failed.Count > 0)
            {
                var at = failed.Max(fact => fact.ResumeCreatedAt ?? fact.AppliedAt);
                notices.Add(Notice(
                    $"unreadable-{vacancy.Id}",
                    $"{Plural(failed.Count, "hoja de vida ilegible", "hojas de vida ilegibles")} en {vacancy.Title}",
                    CandidatesHref(vacancy.Id),
                    "warning",
                    at,
                    now));
            }

            var duplicates = Duplicates(active);
            if (duplicates > 0)
            {
                var at = active.Where(fact => fact.ResumeHash is not null).Max(fact => fact.ResumeCreatedAt ?? fact.AppliedAt);
                notices.Add(Notice(
                    $"duplicate-{vacancy.Id}",
                    $"{Plural(duplicates, "hoja de vida duplicada", "hojas de vida duplicadas")} en {vacancy.Title}",
                    CandidatesHref(vacancy.Id),
                    "warning",
                    at,
                    now));
            }

            var evaluated = active.Where(fact => fact.EvaluatedAt >= since).ToList();
            if (evaluated.Count > 0)
            {
                notices.Add(Notice(
                    $"evaluated-{vacancy.Id}",
                    $"{vacancy.Title}: {Plural(evaluated.Count, "hoja de vida evaluada", "hojas de vida evaluadas")}",
                    $"/rankings/{vacancy.Id}",
                    "success",
                    evaluated.Max(fact => fact.EvaluatedAt!.Value),
                    now));
            }

            var received = active.Where(fact => fact.AppliedAt >= since).ToList();
            if (received.Count > 0)
            {
                notices.Add(Notice(
                    $"received-{vacancy.Id}",
                    $"{Plural(received.Count, "postulación nueva", "postulaciones nuevas")} en {vacancy.Title}",
                    $"/vacancies/{vacancy.Id}",
                    "success",
                    received.Max(fact => fact.AppliedAt),
                    now));
            }
        }

        return notices
            .OrderByDescending(notice => notice.OccurredAt)
            .Take(MaxNotices)
            .ToList();
    }

    /// <summary>"Hace 20 min", "Hace 3 h", "Ayer 17:54" or "10 nov", in Bogotá time.</summary>
    public static string RelativeWhen(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "Ahora";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"Hace {(int)elapsed.TotalMinutes} min";
        }

        var localAt = at.ToOffset(BogotaOffset);
        var localNow = now.ToOffset(BogotaOffset);
        if (localAt.Date == localNow.Date)
        {
            return $"Hace {(int)elapsed.TotalHours} h";
        }

        if (localAt.Date == localNow.Date.AddDays(-1))
        {
            return $"Ayer {localAt:HH:mm}";
        }

        return $"{localAt.Day} {Months[localAt.Month - 1]}";
    }

    /// <summary>"AAAA-MM-DD" in Bogotá time, the date format the front uses; empty when there is no date.</summary>
    public static string LocalDate(DateTimeOffset? value) =>
        value is null ? string.Empty : value.Value.ToOffset(BogotaOffset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static NavNotice Notice(string id, string text, string href, string tone, DateTimeOffset at, DateTimeOffset now) =>
        new(id, text, RelativeWhen(at, now), href, tone, at);

    private static List<ApplicationFact> Active(IEnumerable<ApplicationFact> facts) =>
        facts.Where(fact => !Is(fact.SelectionStatus, "WITHDRAWN")).ToList();

    /// <summary>CVs whose content hash repeats inside the vacancy, not counting the first copy.</summary>
    private static int Duplicates(IEnumerable<ApplicationFact> facts) =>
        facts
            .Where(fact => fact.ResumeHash is not null)
            .GroupBy(fact => fact.ResumeHash, StringComparer.Ordinal)
            .Sum(group => group.Count() - 1);

    private static string CandidatesHref(Guid vacancyId) => $"/candidates?job={vacancyId}";

    private static string Plural(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static bool Is(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
