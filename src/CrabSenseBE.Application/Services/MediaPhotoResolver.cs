using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Tải bytes ảnh từ Drive/S3/local hoặc HTTP theo URL đã lưu DB.</summary>
public static class MediaPhotoResolver
{
    public static async Task<CrabImageContent?> OpenAsync(
        IPublicImageStorage storage,
        string url,
        MediaAsset? asset,
        CancellationToken ct = default)
    {
        var contentType = string.IsNullOrWhiteSpace(asset?.ContentType)
            ? GuessContentType(url, asset?.FileName)
            : asset!.ContentType;
        var fileName = asset?.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Uri.TryCreate(url, UriKind.Absolute, out var nameUri)
                ? Path.GetFileName(Uri.UnescapeDataString(nameUri.AbsolutePath))
                : Path.GetFileName(url);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "photo.jpg";
        }

        // Never derive storage keys from client-controlled URLs or fetch arbitrary
        // HTTP/file URLs. Callers must resolve an authorized MediaAsset first.
        if (asset is null || string.IsNullOrWhiteSpace(asset.StorageKey)) return null;
        try
        {
            await using var stream = await storage.DownloadAsync(asset.StorageKey, ct);
            var copy = new MemoryStream();
            await stream.CopyToAsync(copy, ct);
            copy.Position = 0;
            if (copy.Length > 0) return new CrabImageContent(copy, contentType, fileName);
            copy.Dispose();
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
        return null;
    }

    public static string PublicUrl(MediaUploadResult uploaded)
        => uploaded.ShareLink ?? uploaded.WebContentLink ?? uploaded.WebViewLink
           ?? throw AppException.BadRequest("Upload succeeded but no public URL was returned.");

    private static IEnumerable<string> CandidateStorageKeys(string url, string? assetKey)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(assetKey) && seen.Add(assetKey))
            yield return assetKey;

        foreach (var extracted in ExtractKeysFromUrl(url))
        {
            if (seen.Add(extracted))
                yield return extracted;
        }
    }

    private static IEnumerable<string> ExtractKeysFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.IsFile)
            yield break;

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                var driveId = Uri.UnescapeDataString(kv[1]);
                if (!string.IsNullOrWhiteSpace(driveId))
                    yield return driveId;
            }
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(path)) yield break;
        yield return path;

        var slash = path.IndexOf('/');
        if (slash > 0)
            yield return path[(slash + 1)..];

        const string fileMarker = "file/d/";
        var idx = path.IndexOf(fileMarker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rest = path[(idx + fileMarker.Length)..];
            var end = rest.IndexOf('/');
            yield return end > 0 ? rest[..end] : rest;
        }
    }

    private static string GuessContentType(string url, string? fileName)
    {
        var name = fileName ?? url;
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".heic" or ".heif" => "image/heic",
            _ => "image/jpeg"
        };
    }
}
