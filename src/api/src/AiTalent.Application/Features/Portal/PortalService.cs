using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Rankings;

namespace AiTalent.Application.Features.Portal;

public sealed record PortalMatch(string Level, IReadOnlyList<string> Reasons);

public sealed record PortalJobResponse(
    Guid Id,
    string Title,
    string Company,
    string Area,
    string? Location,
    string? Modality,
    string? ClosesAt,
    string? PostedAt,
    IReadOnlyList<string> Highlights,
    bool Applied,
    PortalMatch? Match);

public sealed record ApplicantResponse(string Name, string Email, string? Interest);

public sealed record PortalHistoryEntry(string Stage, string At);

public sealed record PortalApplicationResponse(
    Guid Id,
    Guid JobId,
    string JobTitle,
    string Company,
    string? Location,
    string? Modality,
    string AppliedAt,
    string UpdatedAt,
    string Stage,
    string Outcome,
    string Note,
    string? InterviewToken,
    string? InterviewDue,
    IReadOnlyList<PortalHistoryEntry> History);

public sealed record PortalResumeResponse(
    string FileName,
    int SizeKb,
    string UpdatedAt,
    ResumeDocResponse Doc,
    IReadOnlyList<string> Skills);

/// <summary>
/// What a person who applied sees: their applications by stage (never scores), their CV and the open
/// vacancies of every company, with how close each one is to their CV. The person is the e-mail of their
/// magic-link session; candidate rows are per company, so all rows with that e-mail are theirs.
/// </summary>
public sealed class PortalService(ICandidateSideStore store, ICvFileStorage storage, IAuditLog audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<PortalJobResponse>> ListOpenJobsAsync(string? email, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var jobs = await store.ListPublicJobsAsync(now, ct);
        var requirements = (await store.ListPublicRequirementsAsync(jobs.Select(job => job.Id).ToList(), ct))
            .GroupBy(requirement => requirement.JobId)
            .ToDictionary(group => group.Key, group => group.ToList());

        HashSet<Guid> applied = [];
        IReadOnlyList<string> skills = [];
        if (!string.IsNullOrWhiteSpace(email))
        {
            applied = (await store.ListApplicantApplicationsAsync(email, ct))
                .Where(application => application.SelectionStatus != "WITHDRAWN")
                .Select(application => application.JobId)
                .ToHashSet();
            skills = ResumeDocBuilder.Skills((await store.FindLatestResumeAsync(email, ct))?.ProfessionalData);
        }

        var list = jobs.Select(job =>
        {
            var reqs = requirements.GetValueOrDefault(job.Id) ?? [];
            return new PortalJobResponse(
                job.Id,
                job.Title,
                job.Company,
                job.Area ?? string.Empty,
                job.Location,
                job.Modality,
                Dates.Day(job.ClosingAt),
                Dates.Day(job.PublishedAt),
                reqs.Where(requirement => requirement.Type == "MANDATORY").Take(3).Select(requirement => requirement.Description).ToList(),
                applied.Contains(job.Id),
                skills.Count == 0 ? null : MatchOf(skills, reqs));
        });

        return skills.Count == 0
            ? list.ToList()
            : list.OrderBy(job => job.Match!.Level switch { "alta" => 0, "media" => 1, _ => 2 }).ToList();
    }

    public async Task<ApplicantResponse> GetApplicantAsync(string email, string? sessionName, CancellationToken ct = default)
    {
        var applications = await store.ListApplicantApplicationsAsync(email, ct);
        var name = applications.FirstOrDefault()?.CandidateName ?? sessionName ?? email.Split('@')[0];
        var interest = applications
            .Where(application => !string.IsNullOrWhiteSpace(application.JobArea))
            .GroupBy(application => application.JobArea!)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        return new ApplicantResponse(name, email, interest);
    }

    public async Task<IReadOnlyList<PortalApplicationResponse>> ListApplicationsAsync(string email, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return (await store.ListApplicantApplicationsAsync(email, ct)).Select(row => Map(row, now)).ToList();
    }

    public async Task<Result<PortalApplicationResponse>> GetApplicationAsync(string email, Guid id, CancellationToken ct = default)
    {
        var row = (await store.ListApplicantApplicationsAsync(email, ct)).FirstOrDefault(application => application.ApplicationId == id);
        return row is null
            ? Result<PortalApplicationResponse>.Failure(ErrorKind.NotFound, "Application not found.")
            : Result<PortalApplicationResponse>.Success(Map(row, clock.GetUtcNow()));
    }

    public async Task<Result<PortalApplicationResponse>> WithdrawAsync(Guid userId, string email, Guid id, CancellationToken ct = default)
    {
        if (!await store.WithdrawAsync(email, id, ct))
        {
            return Result<PortalApplicationResponse>.Failure(ErrorKind.Conflict, "This application can no longer be withdrawn.");
        }

        await audit.RecordAsync(userId, "application.withdrawn", "application", id.ToString(), new { summary = "La persona retiró su postulación" }, ct);
        return await GetApplicationAsync(email, id, ct);
    }

    public async Task<Result<PortalResumeResponse>> GetResumeAsync(string email, CancellationToken ct = default)
    {
        var resume = await store.FindLatestResumeAsync(email, ct);
        if (resume is null)
        {
            return Result<PortalResumeResponse>.Failure(ErrorKind.NotFound, "No CV yet.");
        }

        return Result<PortalResumeResponse>.Success(new PortalResumeResponse(
            resume.FileName ?? "hoja_de_vida.pdf",
            (int)Math.Ceiling((resume.SizeBytes ?? 0) / 1024d),
            Dates.Day(resume.CreatedAt)!,
            ResumeDocBuilder.Build(resume.CandidateName, [email], resume.ProfessionalData),
            ResumeDocBuilder.Skills(resume.ProfessionalData)));
    }

    public async Task<Result<CvFile>> GetResumeFileAsync(string email, CancellationToken ct = default)
    {
        var resume = await store.FindLatestResumeAsync(email, ct);
        var file = resume is null ? null : await storage.DownloadAsync(resume.FileKey, ct);
        return file is null
            ? Result<CvFile>.Failure(ErrorKind.NotFound, "CV file not found.")
            : Result<CvFile>.Success(file);
    }

    /// <summary>Habeas Data: everything stored about the person, as JSON.</summary>
    public async Task<object> ExportAsync(string email, CancellationToken ct = default)
    {
        var applications = await ListApplicationsAsync(email, ct);
        var resume = await GetResumeAsync(email, ct);
        return new { email, applications, resume = resume.IsSuccess ? resume.Value : null, exportedAt = clock.GetUtcNow() };
    }

    /// <summary>Habeas Data: erases the person's candidate rows (CVs, applications, evaluations and interviews cascade).</summary>
    public async Task<int> EraseAsync(Guid userId, string email, CancellationToken ct = default)
    {
        var erased = await store.EraseAsync(email, ct);
        await audit.RecordAsync(userId, "candidate.erased", "candidate", userId.ToString(), new { summary = "La persona borró sus datos", rows = erased }, ct);
        return erased;
    }

    internal static PortalMatch MatchOf(IReadOnlyList<string> skills, IReadOnlyList<PublicJobRequirementRow> requirements)
    {
        var text = string.Join(" ", requirements.Select(requirement => $"{requirement.Description} {string.Join(" ", requirement.Equivalences)}")).ToLowerInvariant();
        var reasons = skills
            .Where(skill => !skill.EndsWith("de experiencia", StringComparison.Ordinal))
            .Where(skill => skill.Length >= 2 && ContainsWord(text, skill.ToLowerInvariant()))
            .Take(4)
            .ToList();
        var level = reasons.Count >= 3 ? "alta" : reasons.Count >= 1 ? "media" : "baja";
        return new PortalMatch(level, reasons);
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = text.IndexOf(word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + word.Length >= text.Length || !char.IsLetterOrDigit(text[index + word.Length]);
            if (before && after)
            {
                return true;
            }

            index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>Selection status → what the person sees: received, review, interview or decision. Never scores.</summary>
    private static PortalApplicationResponse Map(ApplicantApplicationRow row, DateTimeOffset now)
    {
        var interviewOpen = row.InterviewStatus is "SCHEDULED" or "IN_PROGRESS"
                            && row.InterviewToken is not null
                            && (row.InterviewExpiresAt is null || row.InterviewExpiresAt > now);

        var (stage, outcome) = row.SelectionStatus switch
        {
            "HIRED" => ("decision", "selected"),
            "REJECTED" => ("decision", "closed"),
            "WITHDRAWN" => (row.EvaluatedAt is null ? "received" : "review", "withdrawn"),
            "INTERVIEW" => ("interview", "open"),
            "SHORTLISTED" => ("review", "open"),
            _ => (row.EvaluatedAt is null ? "received" : "review", "open"),
        };

        var note = (stage, outcome) switch
        {
            (_, "withdrawn") => "Retiraste esta postulación. Puedes volver a postularte mientras la vacante siga abierta.",
            (_, "selected") => "¡Te eligieron! La empresa te va a escribir para los siguientes pasos.",
            (_, "closed") => "Esta vez el proceso siguió con otras personas. Tu hoja de vida queda guardada por si se abre algo parecido.",
            ("interview", _) when interviewOpen => "Te invitaron a una entrevista corta por chat. Respóndela cuando te quede bien, antes de que venza el enlace.",
            ("interview", _) when row.InterviewStatus == "COMPLETED" => "Respondiste la entrevista. El equipo la está revisando.",
            ("interview", _) => "Estás en la etapa de entrevista. La empresa te va a escribir.",
            ("review", _) => "El equipo está revisando las postulaciones. Te escribimos apenas haya una decisión.",
            _ => "Recibimos tu hoja de vida. Pronto entra a revisión.",
        };

        var history = new List<PortalHistoryEntry> { new("received", Dates.Day(row.AppliedAt)!) };
        if (row.EvaluatedAt is not null || row.SelectionStatus is "SHORTLISTED" or "INTERVIEW" or "HIRED" or "REJECTED")
        {
            history.Add(new PortalHistoryEntry("review", Dates.Day(row.EvaluatedAt ?? row.UpdatedAt)!));
        }

        if (row.InterviewCreatedAt is not null || row.SelectionStatus == "INTERVIEW")
        {
            history.Add(new PortalHistoryEntry("interview", Dates.Day(row.InterviewCreatedAt ?? row.UpdatedAt)!));
        }

        if (row.SelectionStatus is "HIRED" or "REJECTED")
        {
            history.Add(new PortalHistoryEntry("decision", Dates.Day(row.UpdatedAt)!));
        }

        return new PortalApplicationResponse(
            row.ApplicationId,
            row.JobId,
            row.JobTitle,
            row.Company,
            row.Location,
            row.Modality,
            Dates.Day(row.AppliedAt)!,
            Dates.Day(row.UpdatedAt)!,
            stage,
            outcome,
            note,
            interviewOpen && outcome == "open" ? row.InterviewToken : null,
            interviewOpen && outcome == "open" ? Dates.Day(row.InterviewExpiresAt) : null,
            history);
    }
}

/// <summary>Calendar days as the web app shows them: yyyy-MM-dd in Bogotá time.</summary>
public static class Dates
{
    private static readonly TimeZoneInfo Bogota = FindBogota();

    public static string? Day(DateTimeOffset? value) =>
        value is null ? null : TimeZoneInfo.ConvertTime(value.Value, Bogota).ToString("yyyy-MM-dd");

    private static TimeZoneInfo FindBogota()
    {
        foreach (var id in new[] { "America/Bogota", "SA Pacific Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Bogota", TimeSpan.FromHours(-5), "Bogota", "Bogota");
    }
}
