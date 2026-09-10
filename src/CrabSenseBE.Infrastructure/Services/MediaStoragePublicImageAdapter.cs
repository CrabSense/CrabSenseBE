using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>Wraps Drive/Local media storage when AwsS3:Bucket is not set.</summary>
public sealed class MediaStoragePublicImageAdapter : IPublicImageStorage
{
    private readonly IMediaStorageService _inner;

    public MediaStoragePublicImageAdapter(IMediaStorageService inner) => _inner = inner;

    public string ProviderName => _inner.ProviderName;

    public Task<MediaUploadResult> UploadAsync(
        Stream data, string fileName, string contentType, string folder, CancellationToken ct = default)
        => _inner.UploadAsync(data, fileName, contentType, string.IsNullOrWhiteSpace(folder) ? "image" : folder, ct);

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
        => _inner.DownloadAsync(storageKey, ct);
}
