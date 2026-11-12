using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class CrabImageService : ICrabImageService
{
    private readonly IUnitOfWork _uow;
    private readonly IMediaStorageService _storage;
    private readonly IPublicImageStorage _legacyStorage;
    private readonly FarmAreaScope _areas;
    private readonly MediaAssetAreaGuard _mediaGuard;

    public CrabImageService(
        IUnitOfWork uow,
        IPublicImageStorage legacyStorage,
        ICurrentUserService currentUser,
        IMediaStorageService storage)
    {
        _uow = uow;
        _storage = storage;
        _legacyStorage = legacyStorage;
        _areas = new FarmAreaScope(uow, currentUser);
        _mediaGuard = new MediaAssetAreaGuard(uow, currentUser);
    }

    public async Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadAsync(
        IReadOnlyList<CrabImageFile> files,
        Guid? crabId,
        Guid? harvestVoucherId,
        Guid? uploadedBy,
        CancellationToken ct = default,
        bool replace = false)
    {
        ImageUploadRules.ValidateBatch(files);
        var actor = await _areas.CurrentUserAsync(ct) ?? throw AppException.Unauthorized();
        if (actor.Role == CrabSenseBE.Domain.Enums.UserRole.SystemAdministrator)
            throw AppException.Forbidden("Không có quyền ảnh nghiệp vụ trại.");
        uploadedBy = actor.Id;
        foreach (var file in files)
        {
            MediaService.ValidateFileName(file.FileName, "image");
            MediaService.ValidateContent(file.Data, "image", file.ContentType);
        }

        HarvestVoucher? voucher = null;
        if (harvestVoucherId is Guid voucherId && voucherId != Guid.Empty)
        {
            voucher = await _uow.HarvestVouchers.GetByIdAsync(voucherId, ct)
                ?? throw AppException.NotFound("HarvestVoucher");
            if (voucher.FarmingAreaId is not Guid voucherAreaId
                || voucherAreaId == Guid.Empty)
                throw AppException.Forbidden("Không xác định được khu của phiếu thu hoạch.");
            await _areas.EnsureAsync(voucherAreaId, manage: false, ct);
            var voucherUrls = JsonStringList.Parse(voucher.PhotoUrlsJson);
            if ((replace ? 0 : voucherUrls.Count) + files.Count > ImageUploadRules.MaxUrls)
                throw AppException.BadRequest($"A harvest voucher can have at most {ImageUploadRules.MaxUrls} images.");
        }

        Crab? crab = null;
        if (crabId is Guid id && id != Guid.Empty)
        {
            crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
            await EnsureCrabAreaAsync(crab, ct);
            var existing = JsonStringList.Parse(crab.ImageUrlsJson);
            if ((replace ? 0 : existing.Count) + files.Count > ImageUploadRules.MaxUrls)
                throw AppException.BadRequest($"A crab can have at most {ImageUploadRules.MaxUrls} images.");
        }

        var lineCrabs = new List<Crab>();
        if (voucher is not null)
        {
            var lineCrabIds = (await _uow.HarvestLines.FindAsync(
                l => l.HarvestVoucherId == voucher.Id && l.CrabId != null, ct))
                .Select(l => l.CrabId!.Value).Distinct().ToArray();
            if (crab is not null && !lineCrabIds.Contains(crab.Id))
                throw AppException.BadRequest("Cua không thuộc phiếu thu hoạch.");
            foreach (var lineCrabId in lineCrabIds)
            {
                var lineCrab = await _uow.Crabs.GetByIdAsync(lineCrabId, ct)
                    ?? throw AppException.NotFound("Crab");
                await EnsureCrabAreaAsync(lineCrab, ct);
                lineCrabs.Add(lineCrab);
            }
        }

        var folder = voucher is not null
            ? await MediaFolderCodeResolver.ResolvePathAsync(_uow, "harvest-voucher", voucher.Id, ct)
            : crab is null
                ? MediaFolderPath.Join(MediaFolderPath.InboundRoot, MediaFolderPath.PendingSegment)
                : await MediaFolderCodeResolver.ResolvePathAsync(_uow, "crab", crab.Id, ct);
        var results = new List<CrabImageDto>(files.Count);
        var urls = new List<string>(files.Count);

        var stored = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var safeName = MediaService.ValidateFileName(file.FileName, "image");
                var contentType = MediaService.ValidateContent(file.Data, "image", file.ContentType);
                var uploaded = await _storage.UploadAsync(file.Data, safeName, contentType, folder, ct, publicRead: false);
                stored.Add(uploaded.StorageKey);
                var asset = new MediaAsset
                {
                    Category = "image", FileName = safeName, ContentType = contentType,
                    SizeBytes = uploaded.SizeBytes, Provider = _storage.ProviderName,
                    StorageKey = uploaded.StorageKey, IsShared = false,
                    CrabId = crab?.Id, BoxId = crab?.BoxId,
                    RelatedEntityType = voucher is not null ? "HarvestVoucher" : crab is null ? "crab-pending" : "crab",
                    RelatedEntityId = voucher?.Id ?? crab?.Id,
                    Notes = voucher is not null ? "harvest-photo" : "crab-image", UploadedBy = uploadedBy
                };
                var url = MediaReferenceGuard.ContentUrl(asset.Id);
                await _uow.MediaAssets.AddAsync(asset, ct);
                results.Add(new CrabImageDto(url, string.Empty, safeName, _storage.ProviderName, uploaded.SizeBytes));
                urls.Add(url);
            }

            if (voucher is not null)
            {
                voucher.PhotoUrlsJson = JsonStringList.Serialize(replace ? urls :
                    JsonStringList.Merge(voucher.PhotoUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
                _uow.HarvestVouchers.Update(voucher);
                foreach (var lineCrab in lineCrabs.Where(c => c.Id != crab?.Id))
                {
                    lineCrab.ImageUrlsJson = JsonStringList.Serialize(
                        JsonStringList.Merge(lineCrab.ImageUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
                    _uow.Crabs.Update(lineCrab);
                }
            }

            if (crab is not null)
            {
                crab.ImageUrlsJson = JsonStringList.Serialize(replace ? urls :
                    JsonStringList.Merge(crab.ImageUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
                _uow.Crabs.Update(crab);
            }

            await _uow.SaveChangesAsync(ct);
            return ApiResponse<IReadOnlyList<CrabImageDto>>.Ok(results, "Uploaded.");
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            foreach (var key in stored)
                try { await _storage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception cleanup) { errors.Add(cleanup); }
            if (errors.Count > 1) throw new AggregateException("Image upload failed and cleanup was incomplete.", errors);
            throw;
        }
    }

    public async Task<CrabImageContent?> GetPhotoAsync(Guid crabId, int index, CancellationToken ct = default)
    {
        if (index < 0) return null;

        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
        if (crab is null) return null;
        await EnsureCrabAreaAsync(crab, ct);

        var urls = JsonStringList.Parse(crab.ImageUrlsJson).ToList();
        var assets = (await _uow.MediaAssets.FindAsync(m => m.CrabId == crabId
            || (m.RelatedEntityId == crabId && m.RelatedEntityType == "crab"), ct))
            .OrderBy(m => m.CreatedAt).ToList();

        // The saved album is authoritative after a replacement; do not re-add removed photos.
        if (urls.Count == 0)
            urls.AddRange(assets.Select(a => a.IsShared
                ? a.ShareLink ?? a.WebContentLink ?? a.WebViewLink ?? MediaReferenceGuard.ContentUrl(a.Id)
                : MediaReferenceGuard.ContentUrl(a.Id)));
        if (index >= urls.Count) return null;
        var url = urls[index];
        MediaAsset? asset;
        if (MediaReferenceGuard.TryParseContentId(url, out var id))
            asset = assets.FirstOrDefault(a => a.Id == id) ?? await _uow.MediaAssets.GetByIdAsync(id, ct);
        else
            asset = assets.FirstOrDefault(a => a.ShareLink == url || a.WebContentLink == url || a.WebViewLink == url)
                ?? await _uow.MediaAssets.FirstOrDefaultAsync(a => a.ShareLink == url || a.WebContentLink == url || a.WebViewLink == url, ct);
        if (asset is null || asset.Category != "image") return null;
        if (asset.CrabId != crabId && !(asset.RelatedEntityType == "crab" && asset.RelatedEntityId == crabId))
        {
            if (asset.RelatedEntityType != "HarvestVoucher" || asset.RelatedEntityId is not Guid voucherId
                || !await _uow.HarvestLines.AnyAsync(l => l.HarvestVoucherId == voucherId && l.CrabId == crabId, ct))
                throw AppException.Forbidden("Ảnh không thuộc cua hoặc phiếu thu hoạch của cua.");
        }
        // Exact association above is required even if the caller knows the image URL.
        if (asset.RelatedEntityType == "HarvestVoucher") await _mediaGuard.EnsureAsync(asset, ct);
        if (asset.IsShared && asset.Provider != _storage.ProviderName)
            return await MediaPhotoResolver.OpenAsync(_legacyStorage, url, asset, ct);
        if (!asset.IsShared) await _storage.EnsurePrivateAccessAsync(asset.StorageKey, ct);
        var stream = await _storage.DownloadAsync(asset.StorageKey, ct);
        return new CrabImageContent(stream, asset.ContentType, asset.FileName);
    }

    private async Task EnsureCrabAreaAsync(Crab crab, CancellationToken ct)
    {
        if (crab.BoxId is Guid boxId && boxId != Guid.Empty)
        {
            await _areas.EnsureBoxAsync(boxId, manage: false, ct);
            return;
        }

        var allocation = (await _uow.CrabBoxAllocations.FindAsync(
                item => item.CrabId == crab.Id, ct))
            .OrderByDescending(item => item.StartTime)
            .FirstOrDefault();
        if (allocation is not null)
        {
            await _areas.EnsureBoxAsync(allocation.BoxId, manage: false, ct);
            return;
        }

        var lotAreas = await _uow.CrabLotAreas.FindAsync(
            item => item.CrabLotId == crab.CrabLotId, ct);
        await _areas.EnsureAllAssignedAsync(
            lotAreas.Select(item => item.FarmingAreaId), ct);
    }
}
