using CrabSenseBE.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>
/// Fallback khi chưa cấu hình Google Drive — lưu local ./media-storage.
/// Dùng cho dev/test; production nên Provider=GoogleDrive.
/// </summary>
public class LocalMediaStorageService : IMediaStorageService
{
    private readonly string _root;
    private readonly ILogger<LocalMediaStorageService> _logger;

    public string ProviderName => "Local";

    public LocalMediaStorageService(IConfiguration config, ILogger<LocalMediaStorageService> logger)
    {
        _logger = logger;
        _root = config["MediaStorage:LocalRoot"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "media-storage");
        Directory.CreateDirectory(_root);
    }

    public async Task<MediaUploadResult> UploadAsync(
        Stream data, string fileName, string contentType, string category, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var relative = Path.Combine(
            MapCategoryFolder(category),
            now.ToString("yyyy"),
            now.ToString("MM"),
            now.ToString("dd"),
            $"{now:yyyyMMddHHmmss}_{Sanitize(fileName)}");
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var fs = System.IO.File.Create(full);
        await data.CopyToAsync(fs, ct);
        var size = fs.Length;

        var key = relative.Replace('\\', '/');
        var link = $"file:///{full.Replace('\\', '/')}";
        _logger.LogInformation("Local media saved {Key}", key);
        return new MediaUploadResult(key, link, link, link, size);
    }

    private static string MapCategoryFolder(string category) =>
        category.Trim().ToLowerInvariant() switch
        {
            "image" or "images" or "photo" or "photos" => "images",
            "video" or "videos" => "videos",
            "log" or "logs" => "logs",
            _ => "other"
        };

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var full = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        Stream stream = System.IO.File.OpenRead(full);
        return Task.FromResult(stream);
    }

    public Task<string> EnsureShareLinkAsync(string storageKey, CancellationToken ct = default)
    {
        var full = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult($"file:///{full.Replace('\\', '/')}");
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var full = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(full))
            System.IO.File.Delete(full);
        return Task.CompletedTask;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
