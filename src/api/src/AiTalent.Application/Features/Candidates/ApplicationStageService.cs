using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Enums;
using ApplicationEntity = AiTalent.Domain.Entities.Application;

namespace AiTalent.Application.Features.Candidates;

public sealed class ApplicationStageService(
    IJobOpeningRepository vacancies,
    IApplicationRepository applications,
    StaffAccess access,
    IAuditLog audit,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ApplicationEntity>> ChangeStatusAsync(
        Guid actorUserId,
        Guid vacancyId,
        Guid applicationId,
        ApplicationSelectionStatus target,
        string? reason,
        CancellationToken ct = default)
    {
        var member = await access.GetActiveMemberAsync(actorUserId, ct);
        if (!member.IsSuccess)
        {
            return Result<ApplicationEntity>.Failure(member.Error!);
        }

        var vacancy = await vacancies.FindByIdAsync(member.Value!.CompanyId, vacancyId, ct);
        if (vacancy is null)
        {
            return Result<ApplicationEntity>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        if (!Validation.IsOptionalText(reason, Validation.MaxStageReasonLength))
        {
            return Result<ApplicationEntity>.Failure(
                ErrorKind.Invalid,
                $"Reason must be at most {Validation.MaxStageReasonLength} characters.");
        }

        var application = await applications.FindByIdAsync(applicationId, ct);
        if (application is null || application.JobOpeningId != vacancy.Id)
        {
            return Result<ApplicationEntity>.Failure(ErrorKind.NotFound, "Application not found.");
        }

        var from = application.SelectionStatus;
        if (!application.MoveTo(target))
        {
            var message = from == target
                ? $"Application is already {Name(target)}."
                : $"Cannot move an application from {Name(from)} to {Name(target)}.";
            return Result<ApplicationEntity>.Failure(ErrorKind.Conflict, message);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await audit.RecordAsync(
            actorUserId,
            "application.stage_changed",
            "application",
            application.Id.ToString(),
            new
            {
                vacancyId = vacancy.Id,
                from = Name(from),
                to = Name(target),
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            },
            ct);

        return Result<ApplicationEntity>.Success(application);
    }

    private static string Name(ApplicationSelectionStatus status) =>
        status.ToString().ToUpperInvariant();
}
