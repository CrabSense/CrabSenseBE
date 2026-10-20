using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using CrabSenseBE.Application.Interfaces;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>Triển khai IStorageService bằng MinIO (edge HDF5 archive).</summary>
public class MinioStorageService : IStorageService
{
    // ponytail: lazy — thiếu cấu hình MinIO chỉ fail khi thực sự dùng HDF5,
    // không làm 500 các endpoint khác cùng inject IStorageService.
    private readonly Lazy<IMinioClient> _minio;

    public MinioStorageService(IConfiguration config)
        => _minio = new Lazy<IMinioClient>(() => CreateClient(config));

    private static IMinioClient CreateClient(IConfiguration config)
    {
        // Không có credential mặc định: thiếu config thì báo lỗi rõ thay vì dùng minioadmin.
        var endpoint = config["MinIO:Endpoint"];
        var accessKey = config["MinIO:AccessKey"];
        var secretKey = config["MinIO:SecretKey"];
        if (string.IsNullOrWhiteSpace(endpoint)
            || string.IsNullOrWhiteSpace(accessKey)
            || string.IsNullOrWhiteSpace(secretKey))
        {
            throw new InvalidOperationException(
                "MinIO:Endpoint/AccessKey/SecretKey chưa được cấu hình.");
        }

        var useSSL = bool.Parse(config["MinIO:UseSSL"] ?? "false");

        return new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(useSSL)
            .Build();
    }

    private IMinioClient Minio => _minio.Value;

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct = default)
    {
        var exists = await Minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName), ct);
        if (!exists)
            await Minio.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucketName), ct);
    }

    public async Task<string> UploadAsync(string bucketName, string objectName, Stream data,
        string contentType, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(bucketName, ct);
        await Minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType), ct);

        return $"{bucketName}/{objectName}";
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectName, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        await Minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName)
            .WithCallbackStream(s => s.CopyTo(ms)), ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucketName, string objectName, CancellationToken ct = default)
    {
        await Minio.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName), ct);
    }
}
