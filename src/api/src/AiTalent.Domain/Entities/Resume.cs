using AiTalent.Domain.Enums;

namespace AiTalent.Domain.Entities;

/// <summary>Uploaded CV file and its processing state (table app.resumes).</summary>
public sealed class Resume
{
    private Resume()
    {
        FileKey = string.Empty;
    }

    public Resume(Guid candidateId, int version, string fileKey, string? fileHash)
    {
        Id = Guid.NewGuid();
        CandidateId = candidateId;
        Version = version;
        FileKey = fileKey;
        FileHash = fileHash;
        ProcessingStatus = ResumeProcessingStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid CandidateId { get; private set; }

    public int Version { get; private set; }

    /// <summary>Path inside the private Storage bucket `cvs`.</summary>
    public string FileKey { get; private set; }

    /// <summary>SHA-256 hash of the file, used to detect duplicates.</summary>
    public string? FileHash { get; private set; }

    public ResumeProcessingStatus ProcessingStatus { get; private set; }

    public string? ProcessingError { get; private set; }

    /// <summary>Anonymized structured extraction (jsonb).</summary>
    public string? ProfessionalData { get; private set; }

    public string? ExtractorVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkProcessing()
    {
        ProcessingStatus = ResumeProcessingStatus.Processing;
        ProcessingError = null;
    }

    public void MarkProcessed(string? extractorVersion, string? professionalData)
    {
        ProcessingStatus = ResumeProcessingStatus.Processed;
        ProcessingError = null;
        ExtractorVersion = extractorVersion;
        ProfessionalData = professionalData;
    }

    public void MarkFailed(string error)
    {
        ProcessingStatus = ResumeProcessingStatus.Failed;
        ProcessingError = error;
    }
}
