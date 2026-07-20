namespace CrabSenseBE.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(
        string bucketName,
        string objectName,
        Stream data,
        string contentType,
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(
        string bucketName,
        string objectName,
        CancellationToken ct = default);

    Task DeleteAsync(
        string bucketName,
        string objectName,
        CancellationToken ct = default);

    Task EnsureBucketExistsAsync(
        string bucketName,
        CancellationToken ct = default);
}