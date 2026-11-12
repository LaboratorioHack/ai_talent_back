namespace AiTalent.Application.Abstractions;

/// <summary>Stores CV files in the private `cvs` bucket and returns the stored file key.</summary>
public interface ICvFileStorage
{
    Task<string> UploadAsync(string fileKey, Stream content, string contentType, CancellationToken ct = default);
}
