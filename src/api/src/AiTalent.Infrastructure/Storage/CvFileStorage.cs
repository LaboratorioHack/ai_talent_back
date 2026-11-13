using System.Net.Http.Headers;
using AiTalent.Application.Abstractions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AiTalent.Infrastructure.Storage;

internal sealed class CvFileStorage(
    IOptions<R2StorageOptions> options,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : ICvFileStorage
{
    private const string BucketName = "cvs";

    public async Task<string> UploadAsync(string fileKey, Stream content, string contentType, CancellationToken ct = default)
    {
        var settings = options.Value;
        var key = fileKey.TrimStart('/');

        if (settings.UseR2)
        {
            await UploadR2Async(settings, key, content, contentType, ct);
            return key;
        }

        await UploadSupabaseAsync(key, content, contentType, ct);
        return key;
    }

    public async Task<CvFile?> DownloadAsync(string fileKey, CancellationToken ct = default)
    {
        var settings = options.Value;
        var key = fileKey.TrimStart('/');
        return settings.UseR2 ? await DownloadR2Async(settings, key, ct) : await DownloadSupabaseAsync(key, ct);
    }

    private async Task<CvFile?> DownloadSupabaseAsync(string key, CancellationToken ct)
    {
        var supabaseUrl = configuration["SUPABASE_URL"]?.TrimEnd('/');
        var serviceRoleKey = configuration["SUPABASE_SERVICE_ROLE_KEY"];
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            throw new InvalidOperationException("SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required to read CVs from Supabase storage.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{supabaseUrl}/storage/v1/object/{BucketName}/{key}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        request.Headers.TryAddWithoutValidation("apikey", serviceRoleKey);

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new CvFile(bytes, response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    private static async Task<CvFile?> DownloadR2Async(R2StorageOptions settings, string key, CancellationToken ct)
    {
        var config = new AmazonS3Config { ServiceURL = settings.ResolveEndpoint(), ForcePathStyle = true, AuthenticationRegion = "auto" };
        using var client = new AmazonS3Client(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), config);
        try
        {
            using var response = await client.GetObjectAsync(string.IsNullOrWhiteSpace(settings.BucketName) ? BucketName : settings.BucketName, key, ct);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            return new CvFile(buffer.ToArray(), string.IsNullOrWhiteSpace(response.Headers.ContentType) ? "application/octet-stream" : response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task UploadSupabaseAsync(string key, Stream content, string contentType, CancellationToken ct)
    {
        var supabaseUrl = configuration["SUPABASE_URL"]?.TrimEnd('/');
        var serviceRoleKey = configuration["SUPABASE_SERVICE_ROLE_KEY"];

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            throw new InvalidOperationException(
                "SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are required to upload CVs to Supabase storage.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/storage/v1/object/{BucketName}/{key}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        request.Headers.TryAddWithoutValidation("apikey", serviceRoleKey);
        request.Headers.TryAddWithoutValidation("x-upsert", "false");

        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        using var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Failed to upload CV to storage ({(int)response.StatusCode}): {body}");
        }
    }

    private static async Task UploadR2Async(R2StorageOptions settings, string key, Stream content, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.AccessKeyId) || string.IsNullOrWhiteSpace(settings.SecretAccessKey))
        {
            throw new InvalidOperationException("R2 access credentials are required to upload CVs to R2 storage.");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = settings.ResolveEndpoint(),
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
        };

        using var client = new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
            config);

        // Buffer so we can set length/content reliably.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var put = new PutObjectRequest
        {
            BucketName = string.IsNullOrWhiteSpace(settings.BucketName) ? BucketName : settings.BucketName,
            Key = key,
            InputStream = buffer,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            DisablePayloadSigning = true,
        };

        await client.PutObjectAsync(put, ct);
    }
}
