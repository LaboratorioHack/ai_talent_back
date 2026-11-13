using System.Globalization;
using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Portal;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Application.Features.Governance;

public sealed class GovernanceService(
    ICompanyRepository companies,
    IEvaluationReadStore evaluations,
    IGovernanceReadStore audit,
    StaffAccess access,
    TimeProvider clock)
{
    private static readonly IReadOnlyList<GovernanceAttributeResponse> Attributes =
    [
        new("Foto", false, "se borra al leer"),
        new("Edad y fecha de nacimiento", false, "se borra al leer"),
        new("Género", false, "se borra al leer"),
        new("Dirección y ciudad", false, "se borra al leer"),
        new("Nacionalidad", false, "se borra al leer"),
        new("Estado civil", false, "se borra al leer"),
        new("Experiencia", true, "requisitos"),
        new("Educación y certificaciones", true, "requisitos"),
        new("Idiomas", true, "requisitos"),
        new("Competencias técnicas", true, "requisitos y equivalencias"),
    ];

    /// <summary>Periods the screen offers; anything else is rejected.</summary>
    public static readonly int[] PeriodDays = [7, 30, 90];

    public async Task<Result<GovernanceAuditResponse>> GetAuditAsync(Guid actorUserId, int days = 30, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<GovernanceAuditResponse>.Failure(member.Error!);
        }

        if (!PeriodDays.Contains(days))
        {
            return Result<GovernanceAuditResponse>.Failure(ErrorKind.Invalid, "days must be 7, 30 or 90.");
        }

        var since = clock.GetUtcNow().AddDays(-days);
        var company = await companies.FindByIdAsync(member.Value!.CompanyId, ct);
        var evaluationStats = await evaluations.GetGovernanceEvaluationStatsAsync(member.Value.CompanyId, since, ct);
        var humanDecisions = await audit.CountHumanDecisionsAsync(member.Value.CompanyId, since, ct);
        var aiErrors = await audit.CountAiErrorsFlaggedAsync(member.Value.CompanyId, since, ct);
        var entries = await audit.ListForCompanyAsync(member.Value.CompanyId, limit: 120, since, ct);

        return Result<GovernanceAuditResponse>.Success(new GovernanceAuditResponse(
            company?.Name ?? "Tu organización",
            new GovernanceStatsResponse(
                evaluationStats.SensitiveAttributesUsed,
                evaluationStats.ResumesEvaluated,
                evaluationStats.ScoresWithEvidencePercent,
                humanDecisions,
                aiErrors),
            Attributes,
            entries.Select(entry => MapDecision(entry, Dates.Local(clock.GetUtcNow()))).ToList(),
            []));
    }

    /// <summary>Who the screen shows for engine and agent entries: the AI has a name, not "system".</summary>
    public const string CribaAi = "Criba AI";

    private static AuditDecisionResponse MapDecision(AuditLogEntry entry, DateTimeOffset now)
    {
        var who = entry.ActorType switch
        {
            "user" => FormatStaffName(entry.ActorName),
            "engine" or "agent" => CribaAi,
            _ => "Sistema",
        };

        var (subject, status) = ResolveSubjectAndStatus(entry);
        return new AuditDecisionResponse(
            FormatWhen(entry.OccurredAt, now),
            DescribeAction(entry.Action, entry.DetailsJson),
            who,
            subject,
            status);
    }

    private static string FormatStaffName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "Equipo";
        }

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? parts[0] : $"{parts[0]} {parts[1][0]}.";
    }

    internal static string FormatWhen(DateTimeOffset occurredAt, DateTimeOffset now)
    {
        var local = Dates.Local(occurredAt);
        var today = now.Date;
        var date = local.Date;
        if (date == today)
        {
            return $"Hoy {local:HH:mm}";
        }

        if (date == today.AddDays(-1))
        {
            return $"Ayer {local:HH:mm}";
        }

        var culture = CultureInfo.GetCultureInfo("es-CO");
        var dayName = culture.DateTimeFormat.GetAbbreviatedDayName(local.DayOfWeek);
        return char.ToUpper(dayName[0]) + dayName[1..];
    }

    private static (string Subject, string Status) ResolveSubjectAndStatus(AuditLogEntry entry)
    {
        string subject = entry.SubjectName ?? entry.Entity switch
        {
            "job_opening" => TryReadString(entry.DetailsJson, "title") ?? "Vacante",
            "application" => TryReadString(entry.DetailsJson, "candidate") ?? "Postulación",
            "evaluation" => TryReadString(entry.DetailsJson, "candidate") ?? "Evaluación",
            "company" => "Tu organización",
            _ => "Registro",
        };

        // A result chip only where there is a result: the evaluation's fulfillment, or a rejection.
        var status = TryReadString(entry.DetailsJson, "fulfillment") switch
        {
            "MEETS" => "meets",
            "PARTIALLY_MEETS" => "partial",
            "DOES_NOT_MEET" => "does_not_meet",
            _ when TryReadString(entry.DetailsJson, "to") == "REJECTED" => "does_not_meet",
            _ => "none",
        };

        return (subject, status);
    }

    private static string DescribeAction(string action, string detailsJson) =>
        action switch
        {
            "vacancy.created" => "Creó una vacante",
            "vacancy.updated" => "Actualizó una vacante",
            "vacancy.published" => "Publicó una vacante",
            "vacancy.closed" => "Cerró una vacante",
            "vacancy.deleted" => "Eliminó una vacante",
            "vacancy.profile.updated" => "Actualizó el perfil de cargo",
            "vacancy.profile.versioned" => "Creó una versión nueva del perfil",
            "vacancy.profile.created" => "Definió el perfil de cargo",
            "staff.updated" => "Actualizó un miembro del equipo",
            "company.created" => "Creó la organización",
            "staff.joined" => "Se unió al equipo",
            "resume.processed" => "Leyó una hoja de vida",
            "evaluation.requested" => "Pidió una evaluación",
            "evaluation.completed" => "Evaluó una hoja de vida",
            "evaluation.ai_error.flagged" => "Marcó un error de la IA",
            "ranking.generated" => "Pidió el ranking",
            "candidates.compared" => "Comparó candidatos",
            "evidence.searched" => "Buscó evidencia en una hoja de vida",
            "cv.downloaded" => "Descargó una hoja de vida",
            "application.stage_changed" => "Cambió la etapa de una postulación",
            "application.original_requested" => "Pidió la hoja de vida original",
            "application.withdrawn" => "Retiró su postulación",
            "application.interview.invited" => "Invitó a entrevista con IA",
            "candidate.resume.updated" => "Actualizó su hoja de vida",
            "candidate.erased" => "Borró sus datos",
            "role_profile.requirements_proposed" => "Pidió a Criba AI proponer requisitos",
            "role_profile.equivalences_suggested" => "Pidió a Criba AI equivalencias",
            "invite_code.created" => "Creó un código de invitación",
            "assistant.asked" => "Le preguntó a Criba AI",
            _ => TryReadString(detailsJson, "summary") ?? "Registró una acción",
        };

    private static string? TryReadString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
