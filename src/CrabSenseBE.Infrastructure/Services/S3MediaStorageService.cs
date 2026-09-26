using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>
/// Upload ảnh/video lên AWS S3 và trả public HTTPS URL.
/// Cấu hình: AwsS3:Bucket, AwsS3:Region, AwsS3:AccessKey, AwsS3:SecretKey, AwsS3:PublicBaseUrl.
/// </summary>
public class S3MediaStorageService : IMediaStorageService, IPublicImageStorage
{
    private readonly IConfiguration _config;
    private readonly ILogger<S3MediaStorageService> _logger;
    private readonly Lazy<IAmazonS3> _client;

    public string ProviderName => "S3";

    public S3MediaStorageService(IConfiguration config, ILogger<S3MediaStorageService> logger)
    {
        _config = config;
        _logger = logger;
        _client = new Lazy<IAmazonS3>(CreateClient);
    }

    public Task<MediaUploadResult> UploadAsync(
        Stream data, string fileName, string contentType, string category, CancellationToken ct = default)
        => UploadInternalAsync(
            data,
            fileName,
            contentType,
            category.Contains('/', StringComparison.Ordinal) ? category : MapCategoryFolder(category),
            ct);

    Task<MediaUploadResult> IPublicImageStorage.UploadAsync(
        Stream data, string fileName, string contentType, string folder, CancellationToken ct)
        => UploadInternalAsync(data, fileName, contentType, folder, ct);

    private async Task<MediaUploadResult> UploadInternalAsync(
        Stream data, string fileName, string contentType, string folder, CancellationToken ct)
    {
        var bucket = RequireBucket();
        await using var buffer = await BufferAsync(data, ct);
        var key = BuildObjectKey(folder, fileName);
        var region = Region();

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = buffer,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            AutoCloseStream = false
        };

        if (bool.TryParse(_config["AwsS3:PublicRead"], out var publicRead) && publicRead)
            request.CannedACL = S3CannedACL.PublicRead;

        await _client.Value.PutObjectAsync(request, ct);
        var url = PublicUrl(bucket, region, key);
        _logger.LogInformation("S3 uploaded {Key} ({Bytes} bytes) → {Url}", key, buffer.Length, url);
        return new MediaUploadResult(key, url, url, url, buffer.Length);
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var bucket = RequireBucket();
        var response = await _client.Value.GetObjectAsync(bucket, storageKey, ct);
        var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public Task<string> EnsureShareLinkAsync(string storageKey, CancellationToken ct = default)
    {
        var bucket = RequireBucket();
        return Task.FromResult(PublicUrl(bucket, Region(), storageKey));
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var bucket = RequireBucket();
        await _client.Value.DeleteObjectAsync(bucket, storageKey, ct);
    }

    private IAmazonS3 CreateClient()
    {
        var region = RegionEndpoint.GetBySystemName(Region());
        var accessKey = S3Settings.AccessKey(_config);
        var secretKey = S3Settings.SecretKey(_config);
        if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secretKey))
            return new AmazonS3Client(accessKey, secretKey, region);
        return new AmazonS3Client(region);
    }

    private string RequireBucket()
    {
        var bucket = S3Settings.Bucket(_config);
        if (string.IsNullOrWhiteSpace(bucket))
            throw new InvalidOperationException("S3 bucket is not configured (AwsS3:Bucket or S3_BUCKET).");
        return bucket;
    }

    private string Region() => S3Settings.Region(_config);

    private string PublicUrl(string bucket, string region, string key)
    {
        var encodedKey = string.Join("/", key.Split('/').Select(Uri.EscapeDataString));
        var baseUrl = _config["AwsS3:PublicBaseUrl"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return $"{baseUrl}/{encodedKey}";
        return $"https://{bucket}.s3.{region}.amazonaws.com/{encodedKey}";
    }

    private string BuildObjectKey(string folder, string fileName)
    {
        var prefix = (_config["AwsS3:Prefix"] ?? "media").Trim().Trim('/');
        var project = (_config["MediaStorage:ProjectFolder"] ?? "CrabSense").Trim().Trim('/');
        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "Media/image" : folder.Trim().Trim('/');
        if (!safeFolder.Contains('/', StringComparison.Ordinal))
            safeFolder = MapCategoryFolder(safeFolder);
        return $"{prefix}/{project}/{safeFolder}/{Guid.NewGuid():N}_{Sanitize(fileName)}";
    }

    private static string MapCategoryFolder(string category) =>
        category.Trim().ToLowerInvariant() switch
        {
            "image" or "images" or "photo" or "photos" or "crabs" => "Crabs/_pending",
            "video" or "videos" => "Media/video",
            "log" or "logs" => "Media/log",
            _ => SanitizeSegment(category)
        };

    private static async Task<MemoryStream> BufferAsync(Stream data, CancellationToken ct)
    {
        var ms = new MemoryStream();
        if (data.CanSeek) data.Position = 0;
        await data.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private static string SanitizeSegment(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim().Trim('/').Replace("..", "_");
    }
}
