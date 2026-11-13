using System.Security.Cryptography;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;
using AiTalent.Domain.Enums;

namespace AiTalent.Application.Features.Candidates;

public sealed record ApplyToVacancyCommand(
    Guid VacancyId,
    string? Name,
    string? Email,
    bool Consent,
    string FileName,
    string ContentType,
    long Length,
    Func<Stream> OpenReadStream);

public sealed record CandidateApplicationReceipt(
    Guid ApplicationId,
    Guid CandidateId,
    Guid ResumeId,
    string Status,
    bool DuplicateContent);

public sealed class CandidateApplicationService(
    IJobOpeningRepository vacancies,
    ICandidateRepository candidates,
    IResumeRepository resumes,
    IApplicationRepository applications,
    ICvFileStorage storage,
    IAiEngineClient engine,
    IUnitOfWork unitOfWork)
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxConcurrencyRetries = 3;

    public async Task<Result<CandidateApplicationReceipt>> ApplyAsync(
        ApplyToVacancyCommand command,
        CancellationToken ct = default)
    {
        var validation = await ValidateAsync(command, ct);
        if (validation.Error is { } validationError)
        {
            return Result<CandidateApplicationReceipt>.Failure(validationError);
        }

        var vacancy = validation.Value!;
        var name = command.Name!.Trim();
        var email = command.Email!.Trim().ToLowerInvariant();

        byte[] bytes;
        try
        {
            await using var source = command.OpenReadStream();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        catch
        {
            return Invalid("Could not read the uploaded file.");
        }

        if (bytes.LongLength == 0)
        {
            return Invalid("The uploaded file is empty.");
        }

        if (bytes.LongLength > MaxFileSizeBytes)
        {
            return Invalid("CV files must be at most 10 MB.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        // Resolve/create candidate (race-safe on unique company+email).
        var candidate = await GetOrCreateCandidateAsync(vacancy.CompanyId, name, email, ct);
        if (!candidate.IsSuccess)
        {
            return Result<CandidateApplicationReceipt>.Failure(candidate.Error!);
        }

        var candidateId = candidate.Value!;

        // Reapply to same vacancy is not allowed.
        var existingApplication = await applications.FindByJobAndCandidateAsync(vacancy.Id, candidateId, ct);
        if (existingApplication is not null)
        {
            return Result<CandidateApplicationReceipt>.Failure(
                ErrorKind.Conflict,
                "You have already applied to this vacancy.");
        }

        var duplicateContent = await resumes.ExistsWithHashAsync(candidateId, hash, ct);

        // Create resume + application with retries for version/application races.
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Re-check in case of concurrent duplicate apply.
            if (await applications.FindByJobAndCandidateAsync(vacancy.Id, candidateId, ct) is not null)
            {
                return Result<CandidateApplicationReceipt>.Failure(
                    ErrorKind.Conflict,
                    "You have already applied to this vacancy.");
            }

            var resumeId = Guid.NewGuid();
            var version = await resumes.GetNextVersionAsync(candidateId, ct);
            var fileKey = BuildFileKey(vacancy.CompanyId, vacancy.Id, resumeId, command.FileName);
            var contentType = NormalizeContentType(command.ContentType, command.FileName);

            // Upload before DB commit so the engine can read it; on DB failure this may orphan an object (acceptable).
            try
            {
                await using var upload = new MemoryStream(bytes, writable: false);
                await storage.UploadAsync(fileKey, upload, contentType, ct);
            }
            catch (Exception ex)
            {
                return Result<CandidateApplicationReceipt>.Failure(
                    ErrorKind.Unavailable,
                    $"Could not store the CV file: {ex.Message}");
            }

            var resume = new Resume(candidateId, version, fileKey, hash, Path.GetFileName(command.FileName), contentType, bytes.LongLength);
            var application = new Domain.Entities.Application(vacancy.Id, candidateId, ApplicationSource.Candidate);
            resumes.Add(resume);
            applications.Add(application);

            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (DuplicateEntityException)
            {
                // Could be version collision or concurrent application insert; retry if application still absent.
                resumes.Remove(resume);
                applications.Remove(application);

                if (await applications.FindByJobAndCandidateAsync(vacancy.Id, candidateId, ct) is not null)
                {
                    return Result<CandidateApplicationReceipt>.Failure(
                        ErrorKind.Conflict,
                        "You have already applied to this vacancy.");
                }

                continue;
            }

            // Synchronous processing trigger (best effort, status persisted).
            await ProcessAsync(resume, ct);

            return Result<CandidateApplicationReceipt>.Success(new CandidateApplicationReceipt(
                application.Id,
                candidateId,
                resume.Id,
                StatusName(resume.ProcessingStatus),
                duplicateContent));
        }

        return Result<CandidateApplicationReceipt>.Failure(
            ErrorKind.Conflict,
            "Could not submit the application due to a concurrent update. Please try again.");
    }

    private async Task<Result<JobOpening>> ValidateAsync(ApplyToVacancyCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, "Name is required.");
        }

        if (!Validation.IsName(command.Name))
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, $"Name must be at most {Validation.MaxNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(command.Email) || !IsValidEmail(command.Email))
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, "A valid email is required.");
        }

        if (!command.Consent)
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, "Consent to personal data processing is required.");
        }

        if (command.Length <= 0)
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, "The uploaded file is empty.");
        }

        if (command.Length > MaxFileSizeBytes)
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, "CV files must be at most 10 MB.");
        }

        if (!IsAllowedFile(command.FileName, command.ContentType))
        {
            return Result<JobOpening>.Failure(ErrorKind.Invalid, "Only PDF and DOCX files are allowed.");
        }

        var vacancy = await vacancies.FindForApplicationAsync(command.VacancyId, ct);
        if (vacancy is null)
        {
            return Result<JobOpening>.Failure(ErrorKind.NotFound, "Vacancy not found.");
        }

        if (vacancy.Status != JobOpeningStatus.Open)
        {
            return Result<JobOpening>.Failure(ErrorKind.Conflict, "This vacancy is not accepting applications.");
        }

        if (vacancy.ClosingAt is { } closingAt && closingAt <= DateTimeOffset.UtcNow)
        {
            return Result<JobOpening>.Failure(ErrorKind.Conflict, "This vacancy is no longer accepting applications.");
        }

        return Result<JobOpening>.Success(vacancy);
    }

    private async Task<Result<Guid>> GetOrCreateCandidateAsync(Guid companyId, string name, string email, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            var existing = await candidates.FindByCompanyAndEmailAsync(companyId, email, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
                {
                    existing.UpdateName(name);
                    try
                    {
                        await unitOfWork.SaveChangesAsync(ct);
                    }
                    catch (DuplicateEntityException)
                    {
                        // Concurrent update/create; re-read on next loop.
                    }
                }

                return Result<Guid>.Success(existing.Id);
            }

            var candidate = new Candidate(companyId, name, email);
            candidates.Add(candidate);
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
                return Result<Guid>.Success(candidate.Id);
            }
            catch (DuplicateEntityException)
            {
                // Another request created the same (company,email) candidate; retry lookup.
                candidates.Remove(candidate);
            }
        }

        // Final attempt to read.
        var created = await candidates.FindByCompanyAndEmailAsync(companyId, email, ct);
        return created is not null
            ? Result<Guid>.Success(created.Id)
            : Result<Guid>.Failure(ErrorKind.Conflict, "Could not register the candidate. Please try again.");
    }

    private async Task ProcessAsync(Resume resume, CancellationToken ct)
    {
        resume.MarkProcessing();
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Best effort: proceed to ingestion attempt.
        }

        try
        {
            var response = await engine.IngestCvAsync(new IngestCvRequest(resume.Id), ct);
            resume.MarkProcessed(response.ExtractorVersion, SerializeProfile(response.ProfessionalData));
        }
        catch (Exception ex)
        {
            resume.MarkFailed(TruncateError(ex.Message));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Processing status persistence is best-effort; resume remains in latest in-memory state for response.
        }
    }

    private static string SerializeProfile(StructuredCandidateProfile? profile)
    {
        if (profile is null)
        {
            return "{}";
        }

        return System.Text.Json.JsonSerializer.Serialize(profile, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });
    }

    private static string BuildFileKey(Guid companyId, Guid vacancyId, Guid resumeId, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = ".bin";
        }

        ext = ext.ToLowerInvariant();
        return $"{companyId}/{vacancyId}/{resumeId}{ext}";
    }

    private static bool IsAllowedFile(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowedExt = ext is ".pdf" or ".docx";
        if (!allowedExt)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var type = contentType.Trim().ToLowerInvariant();
        return type is "application/pdf"
            or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            or "application/octet-stream";
    }

    private static string NormalizeContentType(string contentType, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".pdf")
        {
            return "application/pdf";
        }

        if (ext == ".docx")
        {
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        }

        return string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email.Trim());
            return string.Equals(addr.Address, email.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string TruncateError(string message)
    {
        const int max = 500;
        var clean = message.ReplaceLineEndings(" ").Trim();
        return clean.Length <= max ? clean : clean[..max];
    }

    private static string StatusName(ResumeProcessingStatus status) => status switch
    {
        ResumeProcessingStatus.Pending => "PENDING",
        ResumeProcessingStatus.Processing => "PROCESSING",
        ResumeProcessingStatus.Processed => "PROCESSED",
        ResumeProcessingStatus.Failed => "FAILED",
        _ => status.ToString().ToUpperInvariant(),
    };

    private static Result<CandidateApplicationReceipt> Invalid(string message) =>
        Result<CandidateApplicationReceipt>.Failure(ErrorKind.Invalid, message);
}
