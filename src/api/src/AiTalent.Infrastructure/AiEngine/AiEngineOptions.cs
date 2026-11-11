namespace AiTalent.Infrastructure.AiEngine;

public sealed class AiEngineOptions
{
    public const string SectionName = "AiEngine";

    public Uri BaseUrl { get; set; } = new("http://localhost:8000/");

    /// <summary>Shared secret sent as X-Internal-Api-Key (defense in depth on the internal network).</summary>
    public string ApiKey { get; set; } = string.Empty;
}
