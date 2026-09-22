using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CrabSenseBE.Application.Common;
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
/// Lưu ảnh / video / log lên Google Drive (My Drive).
/// Cây thư mục: {Bảng}/{Id bản ghi}/file — ví dụ CrabLots/{lotId}/20260922_anh.jpg
/// Gmail không có Shared drives: upload qua Apps Script (DriveApp, quota của bạn).
/// </summary>
public class GoogleDriveMediaStorageService : IMediaStorageService
{
    private static readonly HttpClient WebhookHttp = CreateWebhookClient();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IConfiguration _config;
    private readonly DriveService _drive;
    private readonly string _shareMode;
    private readonly string _projectFolder;
    private readonly ILogger<GoogleDriveMediaStorageService> _logger;
    private readonly ConcurrentDictionary<string, string> _folderCache = new(StringComparer.Ordinal);
    private string? _resolvedRootId;
    private readonly SemaphoreSlim _rootLock = new(1, 1);

    public string ProviderName => "GoogleDrive";

    private string? ConfiguredFolderId
    {
        get
        {
            var id = First(_config, "GoogleDrive:SharedFolderId", "GOOGLE_DRIVE_SHARED_FOLDER_ID");
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (id.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase)) return null;
            return id;
        }
    }

    public GoogleDriveMediaStorageService(
        IConfiguration config,
        ILogger<GoogleDriveMediaStorageService> logger)
    {
        _config = config;
        _logger = logger;
        _shareMode = (First(config, "GoogleDrive:ShareMode", "GOOGLE_DRIVE_SHARE_MODE") ?? "anyone")
            .ToLowerInvariant();
        _projectFolder = First(config, "GoogleDrive:ProjectFolder", "MediaStorage:ProjectFolder")
            ?? "CrabSense";

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
        var relative = string.IsNullOrWhiteSpace(category) ? "Media/image" : category.Trim().Trim('/');
        if (!relative.Contains('/', StringComparison.Ordinal))
            relative = MediaFolderPath.Build(relative, null, relative);

        // Folder share đã tên CrabSense → không tạo thêm tầng CrabSense bên trong.
        var nestProject = string.IsNullOrWhiteSpace(ConfiguredFolderId);
        var underProject = !nestProject
            || string.IsNullOrWhiteSpace(_projectFolder)
            || relative.StartsWith($"{_projectFolder}/", StringComparison.OrdinalIgnoreCase)
            ? relative
            : $"{_projectFolder}/{relative}";

        try
        {
            var viaScript = await TryWebhookUploadAsync(data, fileName, contentType, underProject, ct);
            if (viaScript is not null)
                return viaScript;

            var rootId = await ResolveRootIdAsync(ct);
            _logger.LogInformation("Drive upload path={Path} root={Root}", underProject, rootId);
            var targetFolderId = await EnsureSubFolderAsync(underProject, rootId, ct);

            await using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, ct);
            buffer.Position = 0;
            var size = buffer.Length;

            var meta = new DriveFile
            {
                Name = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Sanitize(fileName)}",
                Parents = new List<string> { targetFolderId },
                Description = $"CrabSense {relative}"
            };

            var request = _drive.Files.Create(meta, buffer, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            request.Fields = "id, webViewLink, webContentLink, size";
            request.SupportsAllDrives = true;

            var progress = await request.UploadAsync(ct);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                if (progress.Exception is Google.GoogleApiException apiEx)
                    throw TranslateDriveError(apiEx);
                throw AppException.BadRequest($"Không tải được ảnh lên Google Drive: {progress.Exception?.Message}");
            }

            var file = request.ResponseBody
                ?? throw AppException.BadRequest("Google Drive không trả về file sau khi upload.");

            string? shareLink = null;
            if (_shareMode == "anyone")
            {
                try
                {
                    shareLink = await EnsureShareLinkAsync(file.Id, ct);
                }
                catch (Google.GoogleApiException shareEx)
                {
                    _logger.LogWarning(shareEx,
                        "Drive public share failed for {FileId}; dùng link trực tiếp (ảnh vẫn xem qua API proxy).",
                        file.Id);
                    shareLink = DirectViewUrl(file.Id);
                }
            }

            var direct = DirectViewUrl(file.Id);
            _logger.LogInformation("Uploaded {File} to Drive id={Id} path={Path}", fileName, file.Id, underProject);

            return new MediaUploadResult(
                file.Id,
                file.WebViewLink ?? direct,
                file.WebContentLink ?? direct,
                shareLink ?? direct,
                size);
        }
        catch (AppException)
        {
            throw;
        }
        catch (Google.GoogleApiException ex)
        {
            throw TranslateDriveError(ex);
        }
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
            // Already shared.
        }

        return DirectViewUrl(storageKey);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var delete = _drive.Files.Delete(storageKey);
        delete.SupportsAllDrives = true;
        await delete.ExecuteAsync(ct);
    }

    private async Task<string> ResolveRootIdAsync(CancellationToken ct)
    {
        var configured = ConfiguredFolderId;
        if (string.IsNullOrWhiteSpace(configured))
            throw AppException.BadRequest(
                "Chưa đặt GoogleDrive:SharedFolderId. Dán Folder ID My Drive CrabSense " +
                "vào GOOGLE_DRIVE_SHARED_FOLDER_ID.");

        if (string.Equals(_resolvedRootId, configured, StringComparison.Ordinal))
            return configured;

        await _rootLock.WaitAsync(ct);
        try
        {
            if (string.Equals(_resolvedRootId, configured, StringComparison.Ordinal))
                return configured;
            await EnsureDriveRootAsync(configured, ct);
            _resolvedRootId = configured;
            return configured;
        }
        finally
        {
            _rootLock.Release();
        }
    }

    private async Task<string> EnsureSubFolderAsync(string relativePath, string parentId, CancellationToken ct)
    {
        var cacheKey = $"{parentId}/{relativePath}";
        if (_folderCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var current = parentId;
        var walked = parentId;
        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var safe = Sanitize(segment);
            walked = $"{walked}/{safe}";
            if (_folderCache.TryGetValue(walked, out var hit))
            {
                current = hit;
                continue;
            }

            current = await FindOrCreateFolderAsync(current, safe, ct);
            _folderCache[walked] = current;
        }

        _folderCache[cacheKey] = current;
        return current;
    }

    private async Task EnsureDriveRootAsync(string folderId, CancellationToken ct)
    {
        try
        {
            var get = _drive.Files.Get(folderId);
            get.Fields = "id, name, driveId, mimeType";
            get.SupportsAllDrives = true;
            var file = await get.ExecuteAsync(ct);
            _logger.LogInformation(
                "Drive root {Name} id={Id} myDrive={MyDrive}",
                file.Name, file.Id, string.IsNullOrWhiteSpace(file.DriveId));
        }
        catch (Google.GoogleApiException ex)
        {
            throw TranslateDriveError(ex);
        }
    }

    private async Task<MediaUploadResult?> TryWebhookUploadAsync(
        Stream data, string fileName, string contentType, string relativePath, CancellationToken ct)
    {
        var url = First(_config, "GoogleDrive:WebhookUrl", "GOOGLE_DRIVE_WEBHOOK_URL");
        var secret = First(_config, "GoogleDrive:WebhookSecret", "GOOGLE_DRIVE_WEBHOOK_SECRET");
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret)
            || secret.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var buffer = new MemoryStream();
        await data.CopyToAsync(buffer, ct);
        var payload = new
        {
            secret,
            filename = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Sanitize(fileName)}",
            mimeType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            dataBase64 = Convert.ToBase64String(buffer.ToArray()),
            folderPath = relativePath,
            shareAnyone = _shareMode == "anyone"
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var response = await PostWebhookAsync(new Uri(url), bytes, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        WebhookReply? body = null;
        if (!LooksLikeHtml(raw))
        {
            try
            {
                body = JsonSerializer.Deserialize<WebhookReply>(raw, JsonOpts);
            }
            catch (JsonException)
            {
                // Apps Script đôi khi bọc thêm trường.
            }
        }

        if (!response.IsSuccessStatusCode || body is null || !body.Success || string.IsNullOrWhiteSpace(body.FileId))
        {
            _logger.LogWarning(
                "Drive webhook HTTP {Status} content-type={Type} body={Body}",
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                raw.Length > 400 ? raw[..400] : raw);

            if (LooksLikeHtml(raw))
            {
                throw AppException.BadRequest(
                    "Apps Script trả trang HTML thay vì JSON. " +
                    "Deploy → Manage deployments → Edit: Execute as = Me, Who has access = Anyone " +
                    "(không chọn Anyone with a Google account). Version = New version → Deploy. " +
                    "Nếu URL /exec đổi thì dán lại vào GOOGLE_DRIVE_WEBHOOK_URL rồi restart API.");
            }

            var detail = body?.Message ?? (raw.Length > 240 ? raw[..240] : raw);
            throw AppException.BadRequest(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Upload Drive webhook thất bại ({(int)response.StatusCode})."
                    : $"Upload Drive webhook thất bại: {detail}");
        }

        var direct = DirectViewUrl(body.FileId);
        _logger.LogInformation("Uploaded {File} via Apps Script id={Id} path={Path}", fileName, body.FileId, relativePath);
        return new MediaUploadResult(
            body.FileId,
            body.Url ?? direct,
            body.WebContentLink ?? direct,
            body.WebContentLink ?? direct,
            buffer.Length);
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(Uri uri, byte[] body, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        var method = HttpMethod.Post;
        var target = uri;
        byte[]? postBody = body;

        for (var hop = 0; hop < 8; hop++)
        {
            response?.Dispose();
            var request = new HttpRequestMessage(method, target);
            if (method == HttpMethod.Post && postBody is not null)
            {
                request.Content = new ByteArrayContent(postBody);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            response = await WebhookHttp.SendAsync(request, ct);
            var code = (int)response.StatusCode;
            if (code is < 300 or >= 400 || response.Headers.Location is not Uri location)
                return response;

            // POST /exec → 302 echo URL. Apps Script trả JSON khi GET URL đó, không phải POST lại.
            target = location.IsAbsoluteUri ? location : new Uri(target, location);
            if (code is 307 or 308)
            {
                method = HttpMethod.Post;
            }
            else
            {
                method = HttpMethod.Get;
                postBody = null;
            }
        }

        return response!;
    }

    private static HttpClient CreateWebhookClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
        return client;
    }

    private static bool LooksLikeHtml(string raw)
        => raw.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);

    private sealed class WebhookReply
    {
        public bool Success { get; set; }
        public string? FileId { get; set; }
        public string? Url { get; set; }
        public string? WebContentLink { get; set; }
        public string? Message { get; set; }
    }

    private async Task<string> FindOrCreateFolderAsync(string parentId, string name, CancellationToken ct)
    {
        var found = await ListChildFolderAsync(parentId, name, "allDrives", ct)
            ?? await ListChildFolderAsync(parentId, name, "user", ct);
        if (found is not null)
            return found;

        var folder = new DriveFile
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = new List<string> { parentId }
        };
        var create = _drive.Files.Create(folder);
        create.Fields = "id";
        create.SupportsAllDrives = true;
        var created = await create.ExecuteAsync(ct);
        return created.Id;
    }

    private async Task<string?> ListChildFolderAsync(string parentId, string name, string corpora, CancellationToken ct)
    {
        try
        {
            var list = _drive.Files.List();
            list.Q = $"name = '{name}' and '{parentId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            list.Fields = "files(id, name)";
            list.PageSize = 10;
            list.SupportsAllDrives = true;
            list.IncludeItemsFromAllDrives = true;
            list.Corpora = corpora;
            var result = await list.ExecuteAsync(ct);
            return result.Files?.FirstOrDefault()?.Id;
        }
        catch (Google.GoogleApiException ex)
        {
            _logger.LogDebug(ex, "Drive list corpora={Corpora} parent={Parent} name={Name}", corpora, parentId, name);
            return null;
        }
    }

    private static string DirectViewUrl(string fileId)
        => $"https://drive.google.com/uc?export=view&id={fileId}";

    public static AppException TranslateDriveError(Google.GoogleApiException ex)
    {
        var raw = ex.Error?.Message ?? ex.Message ?? "";
        var reason = ex.Error?.Errors?.FirstOrDefault()?.Reason ?? "";

        if (reason.Equals("accessNotConfigured", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("has not been used", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("is disabled", StringComparison.OrdinalIgnoreCase))
        {
            return AppException.BadRequest(
                "Chưa bật Google Drive API trên project GCP. " +
                "Mở https://console.developers.google.com/apis/api/drive.googleapis.com/overview?project=646354035422 " +
                "bấm Enable, đợi 1–2 phút rồi upload lại.");
        }

        if (reason.Equals("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("do not have storage quota", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("storage quota has been exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return AppException.BadRequest(
                "Gmail My Drive không cho service account ghi file (quota 0). " +
                "Deploy tools/google_drive_media_upload.gs (Execute as: Me), " +
                "điền GOOGLE_DRIVE_WEBHOOK_URL + GOOGLE_DRIVE_WEBHOOK_SECRET rồi restart API.");
        }

        if (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound
            || reason.Equals("notFound", StringComparison.OrdinalIgnoreCase))
        {
            return AppException.BadRequest(
                "Không tìm thấy folder Google Drive. Tạo folder trên Drive, share cho " +
                "crabsense-drive@crabsense.iam.gserviceaccount.com (Editor), rồi điền Folder ID vào GoogleDrive:SharedFolderId.");
        }

        if (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return AppException.Forbidden(
                "Google Drive từ chối ghi (403" +
                (string.IsNullOrWhiteSpace(reason) ? "" : $", {reason}") +
                "). Với Gmail My Drive hãy deploy Apps Script tools/google_drive_media_upload.gs. Chi tiết: " + raw);
        }

        return AppException.BadRequest($"Google Drive lỗi: {raw}");
    }

    private static GoogleCredential LoadCredential(IConfiguration config)
    {
        var jsonInline = First(config, "GoogleDrive:CredentialsJson", "GOOGLE_DRIVE_CREDENTIALS_JSON");
        if (!string.IsNullOrWhiteSpace(jsonInline))
        {
            return GoogleCredential.FromJson(jsonInline)
                .CreateScoped(DriveService.Scope.Drive);
        }

        var configured = First(config, "GoogleDrive:CredentialsPath", "GOOGLE_DRIVE_CREDENTIALS_PATH")
            ?? "secrets/google-drive-sa.json";

        var path = ResolveCredentialsPath(configured);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException(
                $"Google Drive credentials not found. Tried: {path}. " +
                "Đặt file JSON service account vào CrabSenseBE/crabsense-369f8a964e8a.json hoặc secrets/google-drive-sa.json", path);

        using var stream = System.IO.File.OpenRead(path);
        return GoogleCredential.FromStream(stream)
            .CreateScoped(DriveService.Scope.Drive);
    }

    private static string ResolveCredentialsPath(string configured)
    {
        if (Path.IsPathRooted(configured) && System.IO.File.Exists(configured))
            return configured;

        var cwd = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.GetFullPath(configured),
            Path.GetFullPath(Path.Combine(cwd, configured)),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", configured)),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", configured)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured)),
            Path.GetFullPath(Path.Combine(cwd, "secrets", "google-drive-sa.json")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "secrets", "google-drive-sa.json")),
            Path.GetFullPath(Path.Combine(cwd, "crabsense-369f8a964e8a.json")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "crabsense-369f8a964e8a.json")),
            Path.GetFullPath(Path.Combine(cwd, "..", "..", "..", "crabsense-369f8a964e8a.json")),
        };

        return candidates.FirstOrDefault(System.IO.File.Exists) ?? Path.GetFullPath(Path.Combine(cwd, configured));
    }

    private static string? First(IConfiguration config, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = config[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
            value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace("'", "_").Trim();
    }
}
