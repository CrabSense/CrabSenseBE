using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class CrabImageService : ICrabImageService
{
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
        ImageUploadRules.ValidateBatch(files);

        Crab? crab = null;
        if (crabId is Guid id && id != Guid.Empty)
        {
            crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
            var existing = JsonStringList.Parse(crab.ImageUrlsJson);
            if (existing.Count + files.Count > ImageUploadRules.MaxUrls)
                throw AppException.BadRequest($"A crab can have at most {ImageUploadRules.MaxUrls} images.");
        }

        var folder = crab is null
            ? MediaFolderPath.Join(MediaFolderPath.InboundRoot, MediaFolderPath.PendingSegment)
            : await MediaFolderCodeResolver.ResolvePathAsync(_uow, "crab", crab.Id, ct);
        var results = new List<CrabImageDto>(files.Count);
        var urls = new List<string>(files.Count);

        foreach (var file in files)
        {
            var uploaded = await _storage.UploadAsync(
                file.Data, file.FileName, file.ContentType, folder, ct);

            var url = MediaPhotoResolver.PublicUrl(uploaded);

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
                JsonStringList.Merge(crab.ImageUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
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

        return await MediaPhotoResolver.OpenAsync(_storage, url, assetMatch, ct);
    }
}
