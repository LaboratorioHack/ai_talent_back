using System.Security.Cryptography;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Common;
using AiTalent.Domain.Entities;

namespace AiTalent.Application.Features.Candidates;

public sealed record UploadResumeCommand(string FileName, string ContentType, long Length, Func<Stream> OpenReadStream);

public sealed record UploadResumeReceipt(int Companies, string Status);

/// <summary>
/// The applicant replaces their CV from the portal: one new version per company they applied to
/// (candidate rows are per company), each read again by Criba AI. Later evaluations use it.
/// </summary>
public sealed class ApplicantResumeService(
    ICandidateRepository candidates,
    IResumeRepository resumes,
    ICvFileStorage storage,
    IAiEngineClient engine,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
{
    public async Task<Result<UploadResumeReceipt>> UploadAsync(Guid userId, string email, UploadResumeCommand command, CancellationToken ct = default)
    {
        if (command.Length <= 0 || command.Length > CandidateApplicationService.MaxFileSizeBytes)
        {
            return Result<UploadResumeReceipt>.Failure(ErrorKind.Invalid, "CV files must be between 1 byte and 10 MB.");
        }

        if (!CandidateApplicationService.IsAllowedFile(command.FileName, command.ContentType))
        {
            return Result<UploadResumeReceipt>.Failure(ErrorKind.Invalid, "Only PDF or DOCX files are accepted.");
        }

        var rows = await candidates.ListByEmailAsync(email, ct);
        if (rows.Count == 0)
        {
            return Result<UploadResumeReceipt>.Failure(ErrorKind.NotFound, "Apply to a vacancy first: your CV is kept with your applications.");
        }

        byte[] bytes;
        await using (var source = command.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var contentType = CandidateApplicationService.NormalizeContentType(command.ContentType, command.FileName);
        var extension = Path.GetExtension(command.FileName).ToLowerInvariant();
        var status = "PROCESSED";

        foreach (var candidate in rows)
        {
            var key = $"{candidate.CompanyId}/portal/{Guid.NewGuid()}{extension}";
            await using (var upload = new MemoryStream(bytes, writable: false))
            {
                await storage.UploadAsync(key, upload, contentType, ct);
            }

            var version = await resumes.GetNextVersionAsync(candidate.Id, ct);
            var resume = new Resume(candidate.Id, version, key, hash, Path.GetFileName(command.FileName), contentType, bytes.LongLength);
            resumes.Add(resume);
            await unitOfWork.SaveChangesAsync(ct);
            await ResumeIngestion.ProcessAsync(resume, engine, unitOfWork, ct);
            if (resume.ProcessingStatus != Domain.Enums.ResumeProcessingStatus.Processed)
            {
                status = resume.ProcessingStatus.ToString().ToUpperInvariant();
            }
        }

        await audit.RecordAsync(userId, "candidate.resume.updated", "candidate", userId.ToString(), new { summary = "La persona actualizó su hoja de vida", companies = rows.Count }, ct);
        return Result<UploadResumeReceipt>.Success(new UploadResumeReceipt(rows.Count, status));
    }
}
