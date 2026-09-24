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
    private readonly string _project;
    private readonly ILogger<LocalMediaStorageService> _logger;

    public string ProviderName => "Local";

    public LocalMediaStorageService(IConfiguration config, ILogger<LocalMediaStorageService> logger)
    {
        _logger = logger;
        _root = config["MediaStorage:LocalRoot"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "media-storage");
        Directory.CreateDirectory(_root);
        _project = config["MediaStorage:ProjectFolder"] ?? "CrabSense";
    }

    public async Task<MediaUploadResult> UploadAsync(
        Stream data, string fileName, string contentType, string category, CancellationToken ct = default)
    {
        var folder = string.IsNullOrWhiteSpace(category)
            ? "Media/image"
            : category.Trim().Trim('/');
        if (!folder.Contains('/', StringComparison.Ordinal))
            folder = folder.Trim().ToLowerInvariant() switch
            {
                "image" or "images" or "photo" or "photos" => "Media/image",
                "video" or "videos" => "Media/video",
                "log" or "logs" => "Media/log",
                _ => folder
            };

        var now = DateTime.UtcNow;
        var relative = Path.Combine(
            _project,
            folder.Replace('/', Path.DirectorySeparatorChar),
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
