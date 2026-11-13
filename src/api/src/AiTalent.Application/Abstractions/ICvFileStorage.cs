namespace AiTalent.Application.Abstractions;

/// <summary>A stored CV file read back for the viewer.</summary>
public sealed record CvFile(byte[] Content, string ContentType);

/// <summary>Stores CV files in the private `cvs` bucket and returns the stored file key.</summary>
public interface ICvFileStorage
{
    Task<string> UploadAsync(string fileKey, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Reads a stored CV. Null when the object does not exist.</summary>
    Task<CvFile?> DownloadAsync(string fileKey, CancellationToken ct = default);
}
