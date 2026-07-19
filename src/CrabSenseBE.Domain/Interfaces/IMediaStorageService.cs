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
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default);

    /// <summary>Bật share link (anyone with link) cho file đã upload.</summary>
    Task<string> EnsureShareLinkAsync(string storageKey, CancellationToken ct = default);

    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
