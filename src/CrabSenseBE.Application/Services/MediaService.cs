using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Nghiệp vụ media: lưu metadata DB + file thật trên Google Drive (share folder).
/// category hợp lệ: image | video | log
/// </summary>
public class MediaService : IMediaService
{
    private static readonly HashSet<string> AllowedCategories =
        new(StringComparer.OrdinalIgnoreCase) { "image", "video", "log" };
    private static readonly IReadOnlyDictionary<string, long> MaxBytes =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["image"] = 20L * 1024 * 1024,
            ["video"] = 500L * 1024 * 1024,
            ["log"] = 10L * 1024 * 1024
        };

    private readonly IUnitOfWork _uow;
    private readonly IMediaStorageService _storage;
    private readonly MediaAssetAreaGuard _areaGuard;
    private readonly FarmAreaScope _areas;

    public MediaService(
        IUnitOfWork uow,
        IMediaStorageService storage,
        ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _storage = storage;
        _areaGuard = new MediaAssetAreaGuard(uow, currentUser);
        _areas = new FarmAreaScope(uow, currentUser);
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
        var safeFileName = ValidateFileName(fileName, category);
        var validatedContentType = ValidateContent(
            fileStream, category, contentType);
        if (meta.SharePublic)
            await EnsureCanPublishAsync(ct);

        var entityType = meta.RelatedEntityType
            ?? (meta.CrabId is Guid ? "crab"
                : meta.BoxId is Guid ? "box"
                : meta.DeviceId is Guid ? "device"
                : meta.Category);
        var entityId = meta.RelatedEntityId ?? meta.CrabId ?? meta.BoxId ?? meta.DeviceId;
        await _areaGuard.EnsureAsync(new MediaAsset
        {
            BoxId = meta.BoxId,
            CrabId = meta.CrabId,
            DeviceId = meta.DeviceId,
            RelatedEntityType = meta.RelatedEntityType,
            RelatedEntityId = meta.RelatedEntityId
        }, ct);
        var folder = await MediaFolderCodeResolver.ResolvePathAsync(_uow, entityType, entityId, ct);
        var uploaded = await _storage.UploadAsync(
            fileStream, safeFileName, validatedContentType, folder, ct,
            publicRead: meta.SharePublic);

        try
        {
            string? shareLink = meta.SharePublic ? uploaded.ShareLink : null;
            var isShared = meta.SharePublic && !string.IsNullOrWhiteSpace(shareLink);

            if (meta.SharePublic && !isShared)
            {
                shareLink = await _storage.EnsureShareLinkAsync(uploaded.StorageKey, ct);
                isShared = !string.IsNullOrWhiteSpace(shareLink);
            }

            var asset = new MediaAsset
            {
                Category = category,
                FileName = safeFileName,
                ContentType = validatedContentType,
                SizeBytes = uploaded.SizeBytes,
                Provider = _storage.ProviderName,
                StorageKey = uploaded.StorageKey,
                WebViewLink = meta.SharePublic ? uploaded.WebViewLink : null,
                WebContentLink = meta.SharePublic ? uploaded.WebContentLink : null,
                ShareLink = shareLink,
                IsShared = isShared,
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
        catch (Exception original)
        {
            try
            {
                await _storage.DeleteAsync(
                    uploaded.StorageKey, CancellationToken.None);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(
                    "Media metadata failed and orphan cleanup also failed.",
                    original,
                    cleanup);
            }
            throw;
        }
    }

    public async Task<ApiResponse<IEnumerable<MediaAssetDto>>> ListAsync(
        string? category = null,
        Guid? boxId = null,
        Guid? crabId = null,
        CancellationToken ct = default)
    {
        var query = _uow.MediaAssets.Query().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim().ToLower();
            query = query.Where(a => a.Category.ToLower() == normalizedCategory);
        }
        if (boxId.HasValue)
            query = query.Where(a => a.BoxId == boxId);
        if (crabId.HasValue)
            query = query.Where(a => a.CrabId == crabId);

        var filtered = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        var visible = new List<MediaAsset>();
        foreach (var asset in filtered)
            if (await _areaGuard.CanAccessAsync(asset, ct))
                visible.Add(asset);

        return ApiResponse<IEnumerable<MediaAssetDto>>.Ok(
            visible.Select(Map));
    }

    public async Task<ApiResponse<MediaAssetDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");
        await _areaGuard.EnsureAsync(asset, ct);
        return ApiResponse<MediaAssetDto>.Ok(Map(asset));
    }

    public async Task<ApiResponse<MediaShareResultDto>> ShareAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureCanPublishAsync(ct);
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");
        await _areaGuard.EnsureAsync(asset, ct);

        var link = await _storage.EnsureShareLinkAsync(asset.StorageKey, ct);
        asset.ShareLink = link;
        asset.IsShared = true;
        if (string.IsNullOrWhiteSpace(asset.WebViewLink))
            asset.WebViewLink = link;

        _uow.MediaAssets.Update(asset);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<MediaShareResultDto>.Ok(new MediaShareResultDto(asset.Id, link, true));
    }

    public async Task<MediaContentResult> OpenContentAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");
        await _areaGuard.EnsureAsync(asset, ct);
        if (!asset.IsShared)
            await _storage.EnsurePrivateAccessAsync(asset.StorageKey, ct);
        var content = await _storage.DownloadAsync(asset.StorageKey, ct);
        return new MediaContentResult(
            content,
            string.IsNullOrWhiteSpace(asset.ContentType)
                ? "application/octet-stream"
                : asset.ContentType,
            asset.FileName,
            asset.SizeBytes);
    }

    public async Task<ApiResponse> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await _uow.MediaAssets.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MediaAsset");
        await _areaGuard.EnsureAsync(asset, ct);

        await _storage.DeleteAsync(asset.StorageKey, ct);
        _uow.MediaAssets.Remove(asset);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
    }

    private static MediaAssetDto Map(MediaAsset a) => new(
        a.Id, a.Category, a.FileName, a.ContentType, a.SizeBytes, a.Provider, string.Empty,
        a.WebViewLink, a.ShareLink, a.IsShared, a.BoxId, a.CrabId, a.DeviceId,
        a.RelatedEntityType, a.RelatedEntityId, a.CreatedAt);

    private async Task EnsureCanPublishAsync(CancellationToken ct)
    {
        var user = await _areas.CurrentUserAsync(ct);
        if (user is null) return;
        if (user.Role is not (UserRole.FarmAdministrator or UserRole.FarmManager))
            throw AppException.Forbidden(
                "Chỉ Farm Administrator hoặc Farm Manager được công khai media.");
    }

    internal static string ValidateFileName(string fileName, string category)
    {
        var trimmed = fileName.Trim();
        if (trimmed.Length is 0 or > 180
            || !string.Equals(
                trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal)
            || trimmed.Any(char.IsControl))
            throw AppException.BadRequest("FileName không hợp lệ.");

        var extension = Path.GetExtension(trimmed).ToLowerInvariant();
        var allowed = category switch
        {
            "image" => extension is ".jpg" or ".jpeg" or ".png" or ".webp",
            "video" => extension is ".mp4" or ".webm",
            "log" => extension is ".log" or ".txt" or ".json" or ".csv",
            _ => false
        };
        if (!allowed)
            throw AppException.BadRequest(
                $"Phần mở rộng file không hợp lệ cho category {category}.");
        return trimmed;
    }

    internal static string ValidateContent(
        Stream stream,
        string category,
        string claimedContentType)
    {
        if (!stream.CanSeek)
            throw AppException.BadRequest(
                "Upload stream phải hỗ trợ seek để xác minh kích thước và file signature.");
        var length = stream.Length;
        if (length <= 0 || length > MaxBytes[category])
            throw AppException.BadRequest(
                $"Kích thước {category} phải trong khoảng 1..{MaxBytes[category]} bytes.");

        var claimed = claimedContentType?.Split(';', 2)[0]
            .Trim()
            .ToLowerInvariant();
        var original = stream.Position;
        Span<byte> prefix = stackalloc byte[16];
        var read = stream.Read(prefix);
        stream.Position = original;
        var actual = DetectContentType(prefix[..read], category, claimed);
        if (!string.Equals(actual, claimed, StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest(
                $"Content-Type không khớp file signature; expected {actual}.");
        return actual;
    }

    private static string DetectContentType(
        ReadOnlySpan<byte> bytes,
        string category,
        string? claimed)
    {
        if (category == "image")
        {
            if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
                return "image/jpeg";
            if (bytes.StartsWith(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
                return "image/png";
            if (bytes.Length >= 12
                && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
                return "image/webp";
        }
        else if (category == "video")
        {
            if (bytes.Length >= 12
                && bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
                return "video/mp4";
            if (bytes.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
                return "video/webm";
        }
        else if (category == "log"
                 && !bytes.Contains((byte)0)
                 && claimed is "text/plain" or "application/json" or "text/csv")
        {
            return claimed;
        }

        throw AppException.BadRequest(
            $"File signature không được hỗ trợ cho category {category}.");
    }
}
