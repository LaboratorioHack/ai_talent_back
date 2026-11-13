using System.Text.Json;
using AiTalent.Application.Abstractions;
using AiTalent.Application.Abstractions.AiEngine;
using AiTalent.Domain.Entities;

namespace AiTalent.Application.Features.Candidates;

/// <summary>
/// Asks the AI Engine to read a stored CV (extraction, anonymization, fragments and embeddings) and keeps
/// the outcome on the resume row. Shared by the public application and the applicant's own CV update.
/// </summary>
internal static class ResumeIngestion
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static async Task ProcessAsync(Resume resume, IAiEngineClient engine, IUnitOfWork unitOfWork, CancellationToken ct)
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            resume.MarkFailed(TruncateError(ex.Message));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Processing status persistence is best-effort; the in-memory state is still returned.
        }
    }

    private static string SerializeProfile(StructuredCandidateProfile? profile) =>
        profile is null ? "{}" : JsonSerializer.Serialize(profile, SnakeCase);

    private static string TruncateError(string message)
    {
        const int max = 500;
        var clean = message.ReplaceLineEndings(" ").Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
