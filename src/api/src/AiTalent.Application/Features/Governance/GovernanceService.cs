using System.Globalization;
using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
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
            entries.Select(MapDecision).ToList(),
            []));
    }

    private static AuditDecisionResponse MapDecision(AuditLogEntry entry)
    {
        var who = entry.ActorType switch
        {
            "user" => FormatStaffName(entry.ActorName),
            "engine" or "api" or "agent" => "Sistema",
            _ => "Sistema",
        };

        var (subject, status) = ResolveSubjectAndStatus(entry);
        return new AuditDecisionResponse(
            FormatWhen(entry.OccurredAt),
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

    private static string FormatWhen(DateTimeOffset occurredAt)
    {
        var local = occurredAt.ToLocalTime();
        var today = DateTime.Today;
        var date = local.Date;
        if (date == today)
        {
            return $"Hoy {local:HH:mm}";
        }

        if (date == today.AddDays(-1))
        {
            return "Ayer";
        }

        var culture = CultureInfo.GetCultureInfo("es-CO");
        var dayName = culture.DateTimeFormat.GetAbbreviatedDayName(local.DayOfWeek);
        return char.ToUpper(dayName[0]) + dayName[1..];
    }

    private static (string Subject, string Status) ResolveSubjectAndStatus(AuditLogEntry entry)
    {
        string subject = entry.Entity switch
        {
            "job_opening" => TryReadString(entry.DetailsJson, "title") ?? "Vacante",
            "application" => TryReadString(entry.DetailsJson, "candidate") ?? "Postulación",
            "evaluation" => TryReadString(entry.DetailsJson, "candidate") ?? "Evaluación",
            _ => entry.EntityId ?? entry.Entity,
        };

        var status = entry.Action switch
        {
            var action when action.Contains("discard", StringComparison.OrdinalIgnoreCase) => "does_not_meet",
            var action when action.Contains("interview", StringComparison.OrdinalIgnoreCase) => "meets",
            var action when action.Contains("profile", StringComparison.OrdinalIgnoreCase) => "unevaluated",
            var action when action.Contains("false_positive", StringComparison.OrdinalIgnoreCase)
                || action.Contains("correct", StringComparison.OrdinalIgnoreCase) => "partial",
            _ => "unevaluated",
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
            _ => TryReadString(detailsJson, "summary") ?? action.Replace('.', ' '),
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
