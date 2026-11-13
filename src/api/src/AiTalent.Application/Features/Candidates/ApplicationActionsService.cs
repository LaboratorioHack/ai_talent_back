using System.Net;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Interviews;
using AiTalent.Application.Features.Staff;

namespace AiTalent.Application.Features.Candidates;

public sealed record AiErrorInput(string? Kind, Guid? RequirementId, string? Note);

/// <summary>Recruiter actions on one application besides moving it: read its CV, flag an AI error, ask for the original file.</summary>
public sealed class ApplicationActionsService(
    ICandidateSideStore store,
    ICvFileStorage storage,
    StaffAccess access,
    IEmailSender email,
    IAuditLog audit)
{
    private static readonly Dictionary<string, string> AiErrors = new()
    {
        ["equivalence"] = "Contó una tecnología que no es equivalente",
        ["missed"] = "No vio algo que sí está en la hoja de vida",
        ["years"] = "Calculó mal los años de experiencia",
        ["citation"] = "La cita no dice lo que afirma",
    };

    public async Task<Result<CvFile>> GetResumeFileAsync(Guid actorUserId, Guid vacancyId, Guid applicationId, CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<CvFile>.Failure(member.Error!);
        }

        var key = await store.FindResumeKeyAsync(member.Value!.CompanyId, vacancyId, applicationId, ct);
        var file = key is null ? null : await storage.DownloadAsync(key, ct);
        if (file is null)
        {
            return Result<CvFile>.Failure(ErrorKind.NotFound, "CV file not found.");
        }

        await audit.RecordAsync(actorUserId, "cv.downloaded", "application", applicationId.ToString(), new { }, ct);
        return Result<CvFile>.Success(file);
    }

    public async Task<Result<bool>> FlagAiErrorAsync(Guid actorUserId, Guid vacancyId, Guid applicationId, AiErrorInput input, CancellationToken ct = default)
    {
        var target = await TargetAsync(actorUserId, vacancyId, applicationId, ct);
        if (!target.IsSuccess)
        {
            return Result<bool>.Failure(target.Error!);
        }

        if (input.Kind is null || !AiErrors.TryGetValue(input.Kind, out var label))
        {
            return Result<bool>.Failure(ErrorKind.Invalid, "Kind must be equivalence, missed, years or citation.");
        }

        if (input.Note is { Length: > 500 })
        {
            return Result<bool>.Failure(ErrorKind.Invalid, "The note must be at most 500 characters.");
        }

        await audit.RecordAsync(
            actorUserId,
            "evaluation.ai_error.flagged",
            "evaluation",
            applicationId.ToString(),
            new { summary = $"Marcó un error de la IA: {label}", candidate = target.Value!.CandidateName, kind = input.Kind, requirementId = input.RequirementId, note = input.Note },
            ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> RequestOriginalAsync(Guid actorUserId, Guid vacancyId, Guid applicationId, CancellationToken ct = default)
    {
        var target = await TargetAsync(actorUserId, vacancyId, applicationId, ct);
        if (!target.IsSuccess)
        {
            return Result<bool>.Failure(target.Error!);
        }

        var t = target.Value!;
        try
        {
            await email.SendAsync(new EmailMessage(
                t.CandidateEmail,
                $"Necesitamos tu hoja de vida otra vez · {t.VacancyTitle}",
                CandidateInterviewService.Html(
                    $"Hola, {t.CandidateName.Split(' ')[0]}",
                    $"{WebUtility.HtmlEncode(t.CompanyName)} no pudo leer el archivo que enviaste para <strong>{WebUtility.HtmlEncode(t.VacancyTitle)}</strong>. Por favor vuelve a enviarlo en PDF o Word (no una foto ni un escaneo).",
                    null,
                    null,
                    null),
                null,
                t.CandidateName), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<bool>.Failure(ErrorKind.Unavailable, "The e-mail could not be sent.");
        }

        await audit.RecordAsync(
            actorUserId,
            "application.original_requested",
            "application",
            applicationId.ToString(),
            new { summary = "Pidió el archivo original", candidate = t.CandidateName },
            ct);
        return Result<bool>.Success(true);
    }

    private async Task<Result<InterviewTarget>> TargetAsync(Guid actorUserId, Guid vacancyId, Guid applicationId, CancellationToken ct)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<InterviewTarget>.Failure(member.Error!);
        }

        var target = await store.FindInterviewTargetAsync(member.Value!.CompanyId, vacancyId, applicationId, ct);
        return target is null
            ? Result<InterviewTarget>.Failure(ErrorKind.NotFound, "Application not found.")
            : Result<InterviewTarget>.Success(target);
    }
}
