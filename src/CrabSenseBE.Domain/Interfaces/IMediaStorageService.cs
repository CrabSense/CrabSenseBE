namespace CrabSenseBE.Domain.Interfaces;

/// <summary>
/// Kết quả upload lên kho media (Google Drive / local).
/// </summary>
public record MediaUploadResult(
    string StorageKey,
    string? WebViewLink,
    string? WebContentLink,
    string? ShareLink,
    long SizeBytes);

/// <summary>
/// Lưu ảnh / video / file log — mặc định Google Drive shared folder.
/// Tách khỏi IStorageService (MinIO) vốn dành cho HDF5 edge.
/// </summary>
public interface IMediaStorageService
{
    /// <summary>Provider đang dùng: GoogleDrive | Local</summary>
    string ProviderName { get; }

    Task<MediaUploadResult> UploadAsync(
        Stream data,
        string fileName,
        string contentType,
        string category,
        CancellationToken ct = default,
        bool publicRead = false);

    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Verifies that a private object still belongs to the provider's private
    /// storage boundary before its bytes are opened. Providers with an
    /// intrinsically isolated private bucket/root may keep the default.
    /// </summary>
    Task EnsurePrivateAccessAsync(
        string storageKey,
        CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Bật share link (anyone with link) cho file đã upload.</summary>
    Task<string> EnsureShareLinkAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}

/// <summary>
/// Upload ảnh công khai (AWS S3 khi cấu hình AwsS3:Bucket; fallback Drive/Local).
/// Dùng cho ảnh cua — trả URL ổn định để lưu DB.
/// </summary>
public interface IPublicImageStorage
{
    string ProviderName { get; }

    Task<MediaUploadResult> UploadAsync(
        Stream data,
        string fileName,
        string contentType,
        string folder,
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default);
}
