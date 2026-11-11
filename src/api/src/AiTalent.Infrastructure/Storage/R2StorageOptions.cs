namespace AiTalent.Infrastructure.Storage;

/// <summary>Cloudflare R2 (S3-compatible) for private CV bucket.</summary>
public sealed class R2StorageOptions
{
    public const string SectionName = "R2";

    /// <summary><c>auto</c>, <c>r2</c>, or <c>supabase</c>.</summary>
    public string StorageBackend { get; set; } = "auto";

    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "cvs";
    public string? EndpointUrl { get; set; }
    public string? PublicBaseUrl { get; set; }

    public string ResolveEndpoint() =>
        string.IsNullOrWhiteSpace(EndpointUrl)
            ? $"https://{AccountId}.r2.cloudflarestorage.com"
            : EndpointUrl.TrimEnd('/');

    public bool UseR2 =>
        StorageBackend.Equals("r2", StringComparison.OrdinalIgnoreCase)
        || (StorageBackend.Equals("auto", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(AccessKeyId)
            && !string.IsNullOrWhiteSpace(SecretAccessKey));
}
