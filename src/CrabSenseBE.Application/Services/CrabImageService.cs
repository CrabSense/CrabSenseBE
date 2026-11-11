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
    private readonly FarmAreaScope _areas;

    public CrabImageService(
        IUnitOfWork uow,
        IPublicImageStorage storage,
        ICurrentUserService currentUser)
    {
        _uow = uow;
        _storage = storage;
        _areas = new FarmAreaScope(uow, currentUser);
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
            if (voucherUrls.Count + files.Count > ImageUploadRules.MaxUrls)
                throw AppException.BadRequest($"A harvest voucher can have at most {ImageUploadRules.MaxUrls} images.");
        }

        Crab? crab = null;
        if (crabId is Guid id && id != Guid.Empty)
        {
            crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
            await EnsureCrabAreaAsync(crab, ct);
            var existing = JsonStringList.Parse(crab.ImageUrlsJson);
            if (existing.Count + files.Count > ImageUploadRules.MaxUrls)
                throw AppException.BadRequest($"A crab can have at most {ImageUploadRules.MaxUrls} images.");
        }

        var folder = voucher is not null
            ? await MediaFolderCodeResolver.ResolvePathAsync(_uow, "harvest-voucher", voucher.Id, ct)
            : crab is null
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
                RelatedEntityType = voucher is not null
                    ? "HarvestVoucher"
                    : crab is null ? "crab-pending" : "crab",
                RelatedEntityId = voucher?.Id ?? crab?.Id,
                Notes = voucher is not null ? "harvest-photo" : "crab-image",
                UploadedBy = uploadedBy
            };
            await _uow.MediaAssets.AddAsync(asset, ct);

            results.Add(new CrabImageDto(url, uploaded.StorageKey, file.FileName, _storage.ProviderName, uploaded.SizeBytes));
            urls.Add(url);
        }

        if (voucher is not null)
        {
            voucher.PhotoUrlsJson = JsonStringList.Serialize(
                replace
                    ? urls
                    : JsonStringList.Merge(voucher.PhotoUrlsJson, urls, ImageUploadRules.MaxUrls),
                ImageUploadRules.MaxUrls);
            _uow.HarvestVouchers.Update(voucher);

            // Ảnh thu hoạch cũng là ảnh con cua -> ghi vào album từng con để hồ sơ cua không trống.
            var lineCrabIds = (await _uow.HarvestLines.FindAsync(
                    l => l.HarvestVoucherId == voucher.Id && l.CrabId != null, ct))
                .Select(l => l.CrabId!.Value)
                .Distinct();
            foreach (var lineCrabId in lineCrabIds)
            {
                if (crab is not null && crab.Id == lineCrabId) continue;
                var lineCrab = await _uow.Crabs.GetByIdAsync(lineCrabId, ct);
                if (lineCrab is null) continue;
                await EnsureCrabAreaAsync(lineCrab, ct);
                lineCrab.ImageUrlsJson = JsonStringList.Serialize(
                    JsonStringList.Merge(lineCrab.ImageUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
                _uow.Crabs.Update(lineCrab);
            }
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
        await EnsureCrabAreaAsync(crab, ct);

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
