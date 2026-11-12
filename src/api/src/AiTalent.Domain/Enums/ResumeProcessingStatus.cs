namespace AiTalent.Domain.Enums;

/// <summary>Processing state of an uploaded resume (table app.resumes).</summary>
public enum ResumeProcessingStatus
{
    Pending = 1,
    Processing = 2,
    Processed = 3,
    Failed = 4,
}
