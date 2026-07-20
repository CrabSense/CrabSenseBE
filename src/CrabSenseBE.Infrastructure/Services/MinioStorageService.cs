using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using CrabSenseBE.Application.Interfaces;

namespace CrabSenseBE.Infrastructure.Services;


public class MinioStorageService : IStorageService
{
    private readonly IMinioClient _minio;

    public MinioStorageService(IConfiguration config)
    {
        var endpoint = config["MinIO:Endpoint"] ?? "localhost:9000";
        var accessKey = config["MinIO:AccessKey"] ?? "minioadmin";
        var secretKey = config["MinIO:SecretKey"] ?? "minioadmin";
        var useSSL = bool.Parse(config["MinIO:UseSSL"] ?? "false");

        _minio = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithSSL(useSSL)
            .Build();
    }

    public async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct = default)
    {
        var exists = await _minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(bucketName), ct);
        if (!exists)
            await _minio.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(bucketName), ct);
    }

    public async Task<string> UploadAsync(string bucketName, string objectName, Stream data,
        string contentType, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(bucketName, ct);
        await _minio.PutObjectAsync(new PutObjectArgs()
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
        await _minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName)
            .WithCallbackStream(s => s.CopyTo(ms)), ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucketName, string objectName, CancellationToken ct = default)
    {
        await _minio.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName), ct);
    }
}
