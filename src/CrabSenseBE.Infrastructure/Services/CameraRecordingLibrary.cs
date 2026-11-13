using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.SystemPlatform;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Infrastructure.Services;

/// <summary>Technical camera videos only; never grants access to general farm media.</summary>
public sealed class CameraRecordingLibrary(AppDbContext db, IMediaStorageService storage, ILogger<CameraRecordingLibrary> logger)
{
    public static IQueryable<Device> CameraDevices(AppDbContext db) => db.Devices.AsNoTracking().Where(d =>
        d.DeviceType.ToLower().Contains("cam") || (d.StreamUrl != null && d.StreamUrl.Trim() != "")
        || (d.SnapshotUrl != null && d.SnapshotUrl.Trim() != ""));

    public static IQueryable<MediaAsset> TechnicalVideos(AppDbContext db) => db.MediaAssets.AsNoTracking().Where(x =>
        x.DeviceId.HasValue && CameraDevices(db).Any(d => d.Id == x.DeviceId.Value)
        && x.BoxId == null && x.CrabId == null
        && ((x.RelatedEntityType == null && x.RelatedEntityId == null)
            || (x.RelatedEntityType != null && x.RelatedEntityType.ToLower() == "device" && x.RelatedEntityId == x.DeviceId))
        && (x.Category.ToLower() == "video" || x.ContentType.StartsWith("video/"))
        && !db.AiDetections.Any(a => a.MediaId == x.Id)
        && !db.Inspections.Any(a => a.RelatedMediaId == x.Id)
        && !db.ObservationEvents.Any(a => a.VideoMediaId == x.Id));

    public async Task<RecordingLibraryPage> ListAsync(RecordingLibraryQuery request, CancellationToken ct = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 200
            || (long)(request.Page - 1) * request.PageSize > int.MaxValue)
            throw AppException.BadRequest("Page >= 1; PageSize từ 1 đến 200.");
        if (request.From.HasValue && request.To.HasValue && request.From >= request.To)
            throw AppException.BadRequest("From phải trước To (To là mốc loại trừ).");
        var search = request.Search?.Trim();
        if (search?.Length > 200) throw AppException.BadRequest("Search tối đa 200 ký tự.");

        var cameras = CameraDevices(db);
        if (request.DeviceId is Guid deviceId && !await cameras.AnyAsync(d => d.Id == deviceId, ct))
            throw AppException.NotFound("Camera");
        if (request.FarmingAreaId is Guid areaId && !await db.FarmingAreas.AnyAsync(a => a.Id == areaId, ct))
            throw AppException.NotFound("FarmingArea");
        if (request.FarmingRowId is Guid rowId)
        {
            var row = await db.FarmingRows.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rowId, ct)
                ?? throw AppException.NotFound("FarmingRow");
            if (request.FarmingAreaId.HasValue && row.FarmingAreaId != request.FarmingAreaId.Value)
                throw AppException.BadRequest("Dãy không thuộc khu đã chọn.");
        }

        var query = from asset in TechnicalVideos(db)
                    join camera in cameras on asset.DeviceId equals (Guid?)camera.Id
                    select new { asset, camera };
        if (request.DeviceId.HasValue) query = query.Where(x => x.camera.Id == request.DeviceId);
        if (request.FarmingAreaId.HasValue) query = query.Where(x => x.camera.FarmingAreaId == request.FarmingAreaId);
        if (request.FarmingRowId.HasValue) query = query.Where(x => x.camera.FarmingRowId == request.FarmingRowId);
        if (request.From.HasValue) query = query.Where(x => x.asset.CreatedAt >= request.From.Value.UtcDateTime);
        if (request.To.HasValue) query = query.Where(x => x.asset.CreatedAt < request.To.Value.UtcDateTime);
        if (!string.IsNullOrEmpty(search))
        {
            var key = search.ToLowerInvariant();
            query = query.Where(x => x.asset.FileName.ToLower().Contains(key) || x.camera.DeviceCode.ToLower().Contains(key)
                || (x.camera.Name != null && x.camera.Name.ToLower().Contains(key)));
        }
        var total = await query.CountAsync(ct);
        var bytes = await query.SumAsync(x => (long?)x.asset.SizeBytes, ct) ?? 0;
        var cameraCount = await query.Select(x => x.camera.Id).Distinct().CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.asset.CreatedAt).ThenByDescending(x => x.asset.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        var areaIds = rows.Select(x => x.camera.FarmingAreaId).OfType<Guid>().Distinct().ToArray();
        var rowIds = rows.Select(x => x.camera.FarmingRowId).OfType<Guid>().Distinct().ToArray();
        var areas = await db.FarmingAreas.AsNoTracking().Where(a => areaIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var locations = await db.FarmingRows.AsNoTracking().Where(r => rowIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return new RecordingLibraryPage(rows.Select(x => Map(x.asset, x.camera,
            x.camera.FarmingAreaId is Guid a ? areas.GetValueOrDefault(a) : null,
            x.camera.FarmingRowId is Guid r ? locations.GetValueOrDefault(r) : null)).ToList(),
            total, request.Page, request.PageSize, bytes, cameraCount);
    }

    public async Task<RecordingLibraryItem> DetailAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await RequireAsync(id, ct);
        var camera = await CameraDevices(db).SingleAsync(d => d.Id == asset.DeviceId, ct);
        var area = await db.FarmingAreas.AsNoTracking().Where(a => a.Id == camera.FarmingAreaId).Select(a => a.Name).FirstOrDefaultAsync(ct);
        var row = await db.FarmingRows.AsNoTracking().Where(r => r.Id == camera.FarmingRowId).Select(r => r.Name).FirstOrDefaultAsync(ct);
        return Map(asset, camera, area, row);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenAsync(Guid id, CancellationToken ct = default)
    {
        var asset = await RequireAsync(id, ct);
        ValidateProvider(asset);
        if (!asset.IsShared) await storage.EnsurePrivateAccessAsync(asset.StorageKey, ct);
        var stream = await storage.DownloadAsync(asset.StorageKey, ct);
        // A provider must supply seekable data for reliable HTTP range playback.
        if (!stream.CanSeek)
        {
            await stream.DisposeAsync();
            throw new AppException("Kho media chưa hỗ trợ đọc video dạng seekable.", 503);
        }
        stream.Position = 0;
        var contentType = asset.ContentType.ToLowerInvariant() switch
        {
            "video/mp4" => "video/mp4", "video/webm" => "video/webm",
            "video/quicktime" => "video/quicktime", "video/x-msvideo" => "video/x-msvideo",
            "video/x-matroska" => "video/x-matroska", _ => "application/octet-stream"
        };
        return (stream, contentType, Path.GetFileName(asset.FileName));
    }

    public async Task DeleteAsync(Guid id, Guid actorId, string? ip, CancellationToken ct = default)
    {
        var asset = await RequireAsync(id, ct);
        ValidateProvider(asset);
        // Refuse deleting a physical object referenced by another metadata row.
        if (await db.MediaAssets.AnyAsync(x => x.Id != id && x.Provider == asset.Provider && x.StorageKey == asset.StorageKey, ct))
            throw AppException.Conflict("File đang được bản ghi khác sử dụng.");
        if (!asset.IsShared) await storage.EnsurePrivateAccessAsync(asset.StorageKey, ct);
        // Persist the request before touching external storage. If storage fails, retain metadata.
        db.OperationLogs.Add(Log(actorId, "DeleteCameraRecordingRequested", id, ip));
        await db.SaveChangesAsync(ct);
        try
        {
            await storage.DeleteAsync(asset.StorageKey, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Camera recording deletion failed for {RecordingId}", id);
            db.OperationLogs.Add(Log(actorId, "DeleteCameraRecordingFailed", id, ip));
            await db.SaveChangesAsync(CancellationToken.None);
            throw new AppException("Không xóa được file trong kho media; metadata được giữ lại.", 502);
        }
        db.MediaAssets.Remove(asset);
        db.OperationLogs.Add(Log(actorId, "DeleteCameraRecording", id, ip));
        // The external deletion has completed. Finish metadata and audit even if client disconnects.
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<MediaAsset> RequireAsync(Guid id, CancellationToken ct)
        => await TechnicalVideos(db).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("Camera recording");

    private void ValidateProvider(MediaAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.StorageKey)
            || !string.Equals(asset.Provider, storage.ProviderName, StringComparison.OrdinalIgnoreCase))
            throw new AppException("Kho lưu file không khớp provider hiện tại; cần kiểm tra cấu hình lưu trữ.", 409);
    }

    private static OperationLog Log(Guid actor, string action, Guid id, string? ip) => new()
    {
        UserId = actor, Action = action, EntityType = "CameraRecording", EntityId = id, IpAddress = ip
    };

    private static RecordingLibraryItem Map(MediaAsset asset, Device camera, string? area, string? row) => new(
        asset.Id, camera.Id, camera.DeviceCode, camera.Name, camera.FarmingAreaId, area,
        camera.FarmingRowId, row, asset.FileName, asset.Category, asset.ContentType, asset.SizeBytes,
        asset.CreatedAt, asset.Provider, asset.IsShared);
}
