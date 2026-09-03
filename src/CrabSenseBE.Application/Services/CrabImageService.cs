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
