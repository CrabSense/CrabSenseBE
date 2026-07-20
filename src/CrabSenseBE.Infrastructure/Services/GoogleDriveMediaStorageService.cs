using CrabSenseBE.Domain.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>
/// Lưu ảnh / video / log lên Google Drive Shared Folder.
/// Cách setup:
/// 1) Tạo Service Account trên Google Cloud + bật Drive API
/// 2) Tải JSON key → đặt path vào GoogleDrive:CredentialsPath
/// 3) Tạo folder trên Drive cá nhân/team → Share folder cho email service account (Editor)
/// 4) Copy Folder ID vào GoogleDrive:SharedFolderId
/// 5) ShareMode=anyone → file có link công khai (anyone with link)
/// </summary>
public class GoogleDriveMediaStorageService : IMediaStorageService
{
    private readonly DriveService _drive;
    private readonly string _folderId;
    private readonly string _shareMode;
    private readonly ILogger<GoogleDriveMediaStorageService> _logger;

    public string ProviderName => "GoogleDrive";

    public GoogleDriveMediaStorageService(
        IConfiguration config,
        ILogger<GoogleDriveMediaStorageService> logger)
    {
        _logger = logger;
        _folderId = config["GoogleDrive:SharedFolderId"]
            ?? throw new InvalidOperationException("GoogleDrive:SharedFolderId is required.");
        _shareMode = (config["GoogleDrive:ShareMode"] ?? "anyone").ToLowerInvariant();

        var credential = LoadCredential(config);
        _drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CrabSenseBE"
        });
    }

    public async Task<MediaUploadResult> UploadAsync(
        Stream data, string fileName, string contentType, string category, CancellationToken ct = default)
    {
        // Cây thư mục trên Drive (dưới SharedFolderId):
        //   images/yyyy/MM/dd/
        //   videos/yyyy/MM/dd/
        //   logs/yyyy/MM/dd/
        var rootFolder = MapCategoryFolder(category);
        var now = DateTime.UtcNow;
        var folderPath = $"{rootFolder}/{now:yyyy}/{now:MM}/{now:dd}";
        var targetFolderId = await EnsureSubFolderAsync(folderPath, ct);

        // Copy stream vào MemoryStream để biết Size + Drive có thể seek
        await using var buffer = new MemoryStream();
        await data.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        var size = buffer.Length;

        var meta = new DriveFile
        {
            Name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Sanitize(fileName)}",
            Parents = new List<string> { targetFolderId },
            Description = $"CrabSense {category}"
        };

        var request = _drive.Files.Create(meta, buffer, contentType);
        request.Fields = "id, webViewLink, webContentLink, size";
        request.SupportsAllDrives = true;

        var progress = await request.UploadAsync(ct);
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw new InvalidOperationException($"Drive upload failed: {progress.Exception?.Message}");

        var file = request.ResponseBody
            ?? throw new InvalidOperationException("Drive upload returned empty file.");

        string? shareLink = null;
        if (_shareMode == "anyone")
            shareLink = await EnsureShareLinkAsync(file.Id, ct);

        _logger.LogInformation("Uploaded {File} to Drive id={Id} category={Cat}", fileName, file.Id, category);

        return new MediaUploadResult(
            file.Id,
            file.WebViewLink,
            file.WebContentLink,
            shareLink ?? file.WebViewLink,
            size);
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        var request = _drive.Files.Get(storageKey);
        request.SupportsAllDrives = true;
        await request.DownloadAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task<string> EnsureShareLinkAsync(string storageKey, CancellationToken ct = default)
    {
        // anyone with the link — reader
        var permission = new Permission
        {
            Type = "anyone",
            Role = "reader",
            AllowFileDiscovery = false
        };

        try
        {
            var create = _drive.Permissions.Create(permission, storageKey);
            create.SupportsAllDrives = true;
            await create.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // Đã share rồi — bỏ qua
        }

        var get = _drive.Files.Get(storageKey);
        get.Fields = "webViewLink, webContentLink";
        get.SupportsAllDrives = true;
        var file = await get.ExecuteAsync(ct);
        return file.WebViewLink ?? file.WebContentLink
            ?? $"https://drive.google.com/file/d/{storageKey}/view";
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var delete = _drive.Files.Delete(storageKey);
        delete.SupportsAllDrives = true;
        await delete.ExecuteAsync(ct);
    }

    /// <summary>Tạo cây thư mục con dưới SharedFolderId nếu chưa có.</summary>
    private async Task<string> EnsureSubFolderAsync(string relativePath, CancellationToken ct)
    {
        var parentId = _folderId;
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var safe = Sanitize(segment);
            var list = _drive.Files.List();
            list.Q = $"name = '{safe}' and '{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            list.Fields = "files(id, name)";
            list.SupportsAllDrives = true;
            list.IncludeItemsFromAllDrives = true;
            var found = await list.ExecuteAsync(ct);
            var existing = found.Files?.FirstOrDefault();
            if (existing is not null)
            {
                parentId = existing.Id;
                continue;
            }

            var folder = new DriveFile
            {
                Name = safe,
                MimeType = "application/vnd.google-apps.folder",
                Parents = new List<string> { parentId }
            };
            var create = _drive.Files.Create(folder);
            create.Fields = "id";
            create.SupportsAllDrives = true;
            var created = await create.ExecuteAsync(ct);
            parentId = created.Id;
        }

        return parentId;
    }

    private static GoogleCredential LoadCredential(IConfiguration config)
    {
        var jsonInline = config["GoogleDrive:CredentialsJson"];
        if (!string.IsNullOrWhiteSpace(jsonInline))
        {
            return GoogleCredential.FromJson(jsonInline)
                .CreateScoped(DriveService.Scope.Drive);
        }

        var configured = config["GoogleDrive:CredentialsPath"]
            ?? throw new InvalidOperationException(
                "Set GoogleDrive:CredentialsPath or GoogleDrive:CredentialsJson.");

        var path = ResolveCredentialsPath(configured);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException(
                $"Google Drive credentials not found. Tried: {path}. " +
                "Đặt file tại CrabSenseBE/secrets/google-drive-sa.json", path);

        using var stream = System.IO.File.OpenRead(path);
        return GoogleCredential.FromStream(stream)
            .CreateScoped(DriveService.Scope.Drive);
    }

    /// <summary>Tìm JSON key từ nhiều vị trí (Api cwd, repo root secrets/, BaseDirectory).</summary>
    private static string ResolveCredentialsPath(string configured)
    {
        if (Path.IsPathRooted(configured) && System.IO.File.Exists(configured))
            return configured;

        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.GetFullPath(configured),
            Path.GetFullPath(Path.Combine(cwd, configured)),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", configured)),      // src/Api → repo root
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", configured)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured)),
            Path.GetFullPath(Path.Combine(cwd, "secrets", "google-drive-sa.json")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "secrets", "google-drive-sa.json"))
        };

        return candidates.FirstOrDefault(System.IO.File.Exists) ?? Path.GetFullPath(Path.Combine(cwd, configured));
    }

    /// <summary>Map category API → tên folder trên Drive.</summary>
    private static string MapCategoryFolder(string category) =>
        category.Trim().ToLowerInvariant() switch
        {
            "image" or "images" or "photo" or "photos" => "images",
            "video" or "videos" => "videos",
            "log" or "logs" => "logs",
            _ => "other"
        };

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace("'", "_").Trim();
    }
}
