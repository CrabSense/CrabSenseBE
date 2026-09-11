using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class CrabImageService : ICrabImageService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif", "image/heic", "image/heif"
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".heif"
    };

    private const int MaxFiles = 10;
    private const long MaxBytesPerFile = 10 * 1024 * 1024;
    private const int MaxUrlsPerCrab = 20;

    private readonly IUnitOfWork _uow;
    private readonly IPublicImageStorage _storage;

    public CrabImageService(IUnitOfWork uow, IPublicImageStorage storage)
    {
        _uow = uow;
        _storage = storage;
    }

    public async Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadAsync(
        IReadOnlyList<CrabImageFile> files,
        Guid? crabId,
        Guid? uploadedBy,
        CancellationToken ct = default)
    {
        if (files is null || files.Count == 0)
            throw AppException.BadRequest("At least one image file is required.");
        if (files.Count > MaxFiles)
            throw AppException.BadRequest($"Maximum {MaxFiles} images per upload.");

        Crab? crab = null;
        if (crabId is Guid id && id != Guid.Empty)
        {
            crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
            var existing = JsonStringList.Parse(crab.ImageUrlsJson);
            if (existing.Count + files.Count > MaxUrlsPerCrab)
                throw AppException.BadRequest($"A crab can have at most {MaxUrlsPerCrab} images.");
        }

        var results = new List<CrabImageDto>(files.Count);
        var urls = new List<string>(files.Count);

        foreach (var file in files)
        {
            ValidateFile(file);
            var uploaded = await _storage.UploadAsync(
                file.Data, file.FileName, file.ContentType, "crabs", ct);

            var url = uploaded.ShareLink ?? uploaded.WebContentLink ?? uploaded.WebViewLink
                ?? throw AppException.BadRequest("Upload succeeded but no public URL was returned.");

            var asset = new MediaAsset
            {
                Category = "image",
                FileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType,
                SizeBytes = uploaded.SizeBytes,
                Provider = _storage.ProviderName,
                StorageKey = uploaded.StorageKey,
                WebViewLink = uploaded.WebViewLink,
                WebContentLink = uploaded.WebContentLink,
                ShareLink = url,
                IsShared = true,
                CrabId = crab?.Id,
                RelatedEntityType = crab is null ? "crab-pending" : "crab",
                RelatedEntityId = crab?.Id,
                Notes = "crab-image",
                UploadedBy = uploadedBy
            };
            await _uow.MediaAssets.AddAsync(asset, ct);

            results.Add(new CrabImageDto(url, uploaded.StorageKey, file.FileName, _storage.ProviderName, uploaded.SizeBytes));
            urls.Add(url);
        }

        if (crab is not null)
        {
            crab.ImageUrlsJson = JsonStringList.Serialize(
                JsonStringList.Merge(crab.ImageUrlsJson, urls, MaxUrlsPerCrab), MaxUrlsPerCrab);
            _uow.Crabs.Update(crab);
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<IReadOnlyList<CrabImageDto>>.Ok(results, "Uploaded.");
    }

    public async Task<CrabImageContent?> GetPhotoAsync(Guid crabId, int index, CancellationToken ct = default)
    {
        if (index < 0) return null;

        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
        if (crab is null) return null;

        var urls = JsonStringList.Parse(crab.ImageUrlsJson).ToList();
        var assets = (await _uow.MediaAssets.FindAsync(
                m => m.CrabId == crabId || (m.RelatedEntityId == crabId && m.RelatedEntityType == "crab"),
                ct))
            .OrderBy(m => m.CreatedAt)
            .ToList();

        foreach (var asset in assets)
        {
            var link = asset.ShareLink ?? asset.WebContentLink ?? asset.WebViewLink;
            if (!string.IsNullOrWhiteSpace(link) && !urls.Contains(link, StringComparer.OrdinalIgnoreCase))
                urls.Add(link);
        }

        if (index >= urls.Count) return null;
        var url = urls[index];

        var assetMatch = assets.FirstOrDefault(a =>
            string.Equals(a.ShareLink, url, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.WebContentLink, url, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.WebViewLink, url, StringComparison.OrdinalIgnoreCase));

        if (assetMatch is null && !string.IsNullOrWhiteSpace(url))
        {
            assetMatch = (await _uow.MediaAssets.FindAsync(
                    m => m.ShareLink == url || m.WebContentLink == url || m.WebViewLink == url,
                    ct))
                .FirstOrDefault();
        }

        var contentType = string.IsNullOrWhiteSpace(assetMatch?.ContentType)
            ? GuessContentType(url, assetMatch?.FileName)
            : assetMatch!.ContentType;
        var fileName = assetMatch?.FileName;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Uri.TryCreate(url, UriKind.Absolute, out var nameUri)
                ? Path.GetFileName(Uri.UnescapeDataString(nameUri.AbsolutePath))
                : Path.GetFileName(url);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "photo.jpg";
        }

        var key = assetMatch?.StorageKey;
        if (string.IsNullOrWhiteSpace(key))
            key = TryExtractStorageKey(url);

        foreach (var candidate in CandidateStorageKeys(url, key))
        {
            try
            {
                await using var stream = await _storage.DownloadAsync(candidate, ct);
                var copy = new MemoryStream();
                await stream.CopyToAsync(copy, ct);
                copy.Position = 0;
                if (copy.Length > 0)
                    return new CrabImageContent(copy, contentType, fileName);
            }
            catch
            {
                // Try next key / HTTP / file.
            }
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var httpUri)
            && (httpUri.Scheme == Uri.UriSchemeHttp || httpUri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                using var resp = await http.GetAsync(httpUri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (resp.IsSuccessStatusCode)
                {
                    var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                    if (bytes.Length > 0)
                    {
                        var headerType = resp.Content.Headers.ContentType?.MediaType;
                        return new CrabImageContent(
                            new MemoryStream(bytes),
                            string.IsNullOrWhiteSpace(headerType) ? contentType : headerType,
                            fileName);
                    }
                }
            }
            catch
            {
                // Fall through to file://.
            }
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var path = uri.LocalPath;
            if (File.Exists(path))
                return new CrabImageContent(File.OpenRead(path), contentType, fileName);
        }

        return null;
    }

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

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(path)) yield break;
        yield return path;

        var slash = path.IndexOf('/');
        if (slash > 0)
            yield return path[(slash + 1)..];
    }

    private static string? TryExtractStorageKey(string url)
        => ExtractKeysFromUrl(url).FirstOrDefault();

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

    private static void ValidateFile(CrabImageFile file)
    {
        if (file.Data is null)
            throw AppException.BadRequest("File stream is required.");
        if (string.IsNullOrWhiteSpace(file.FileName))
            throw AppException.BadRequest("File name is required.");

        var ext = Path.GetExtension(file.FileName);
        var typeOk = !string.IsNullOrWhiteSpace(file.ContentType) && AllowedContentTypes.Contains(file.ContentType);
        var extOk = !string.IsNullOrWhiteSpace(ext) && AllowedExtensions.Contains(ext);
        if (!typeOk && !extOk)
            throw AppException.BadRequest($"Unsupported image type '{file.ContentType}'. Use jpeg, png, webp, gif, or heic.");

        if (file.Data.CanSeek && file.Data.Length > MaxBytesPerFile)
            throw AppException.BadRequest($"Each image must be <= {MaxBytesPerFile / (1024 * 1024)} MB.");
    }
}
