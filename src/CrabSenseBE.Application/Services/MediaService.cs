using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Nghiệp vụ media: lưu metadata DB + file thật trên Google Drive (share folder).
/// category hợp lệ: image | video | log
/// </summary>
public class MediaService : IMediaService
{
    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.OrdinalIgnoreCase) { "image", "video", "log" };

    private readonly IUnitOfWork _uow;
    private readonly IMediaStorageService _storage;

    public MediaService(IUnitOfWork uow, IMediaStorageService storage)
    {
        _uow = uow;
        _storage = storage;
    }

    public async Task<ApiResponse<MediaAssetDto>> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        MediaUploadMeta meta,
        Guid? uploadedBy,
        CancellationToken ct = default)
    {
        var category = (meta.Category ?? "image").Trim().ToLowerInvariant();
        if (!AllowedCategories.Contains(category))
            throw AppException.BadRequest("Category phải là image | video | log.");

        if (string.IsNullOrWhiteSpace(fileName))
            throw AppException.BadRequest("fileName is required.");

        var entityType = meta.RelatedEntityType
            ?? (meta.CrabId is Guid ? "crab"
                : meta.BoxId is Guid ? "box"
                : meta.DeviceId is Guid ? "device"
                : meta.Category);
        var entityId = meta.RelatedEntityId ?? meta.CrabId ?? meta.BoxId ?? meta.DeviceId;
        var folder = await MediaFolderCodeResolver.ResolvePathAsync(_uow, entityType, entityId, ct);
        var uploaded = await _storage.UploadAsync(
            fileStream, fileName, contentType, folder, ct);

        string? shareLink = uploaded.ShareLink;
        var isShared = !string.IsNullOrWhiteSpace(shareLink);

        if (meta.SharePublic && !isShared)
        {
            shareLink = await _storage.EnsureShareLinkAsync(uploaded.StorageKey, ct);
            isShared = !string.IsNullOrWhiteSpace(shareLink);
        }

        var asset = new MediaAsset
        {
            Category = category,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = uploaded.SizeBytes,
            Provider = _storage.ProviderName,
            StorageKey = uploaded.StorageKey,
            WebViewLink = uploaded.WebViewLink,
            WebContentLink = uploaded.WebContentLink,
            ShareLink = shareLink ?? uploaded.WebViewLink,
            IsShared = isShared || meta.SharePublic,
            BoxId = meta.BoxId,
            CrabId = meta.CrabId,
            DeviceId = meta.DeviceId,
            RelatedEntityType = meta.RelatedEntityType,
            RelatedEntityId = meta.RelatedEntityId,
            Notes = meta.Notes,
            UploadedBy = uploadedBy
        };

        await _uow.MediaAssets.AddAsync(asset, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<MediaAssetDto>.Ok(Map(asset), "Uploaded.");
    }

    public async Task<ApiResponse<IEnumerable<MediaAssetDto>>> ListAsync(
        string? category = null,
        Guid? boxId = null,
        Guid? crabId = null,
        CancellationToken ct = default)
    {
        var all = await _uow.MediaAssets.GetAllAsync(ct);
        var filtered = all.Where(a =>
            (category is null || a.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            && (boxId is null || a.BoxId == boxId)
            && (crabId is null || a.CrabId == crabId));

        return ApiResponse<IEnumerable<MediaAssetDto>>.Ok(
            filtered.OrderByDescending(a => a.CreatedAt).Select(Map));
    }

    public async Task<ApiResponse<MediaAssetDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");
        return ApiResponse<MediaAssetDto>.Ok(Map(asset));
    }

    public async Task<ApiResponse<MediaShareResultDto>> ShareAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");

        var link = await _storage.EnsureShareLinkAsync(asset.StorageKey, ct);
        asset.ShareLink = link;
        asset.IsShared = true;
        if (string.IsNullOrWhiteSpace(asset.WebViewLink))
            asset.WebViewLink = link;

        _uow.MediaAssets.Update(asset);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<MediaShareResultDto>.Ok(new MediaShareResultDto(asset.Id, link, true));
    }

    public async Task<ApiResponse> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");

        await _storage.DeleteAsync(asset.StorageKey, ct);
        _uow.MediaAssets.Remove(asset);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
    }

    private static MediaAssetDto Map(MediaAsset a) => new(
        a.Id, a.Category, a.FileName, a.ContentType, a.SizeBytes, a.Provider, a.StorageKey,
        a.WebViewLink, a.ShareLink, a.IsShared, a.BoxId, a.CrabId, a.DeviceId,
        a.RelatedEntityType, a.RelatedEntityId, a.CreatedAt);
}
