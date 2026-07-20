namespace CrabSenseBE.Domain.Interfaces;

/// <summary>
/// Lưu file object-storage (MinIO/S3) — dùng cho HDF5 từ Kiosk.
/// Interface nằm ở Domain để Application không phụ thuộc Infrastructure.
/// </summary>
public interface IStorageService
{
    Task<string> UploadAsync(string bucketName, string objectName, Stream data, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string bucketName, string objectName, CancellationToken ct = default);
    Task DeleteAsync(string bucketName, string objectName, CancellationToken ct = default);
    Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct = default);
}
