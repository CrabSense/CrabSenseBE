using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Application.Services;

/// <summary>
/// Full CRUD Area / Row / Box / Crab.
/// Hierarchy on create: Area ← Row ← Box ← Crab (softshell must be placed in a box).
/// Creating a box auto-generates QR.
/// </summary>
public class FarmingService : IFarmingService
{
    private static readonly Regex AreaLetterCodeRegex = new(
        @"^AREA-([A-Z])(\d{2})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AreaNumericCodeRegex = new(
        @"^AREA-(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> AvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/gif"
    };

    private static readonly HashSet<string> AvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };

    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    private readonly IUnitOfWork _uow;
    private readonly IBoxQrService _boxQr;
    private readonly IPublicImageStorage _images;

    public FarmingService(IUnitOfWork uow, IBoxQrService boxQr, IPublicImageStorage images)
    {
        _uow = uow;
        _boxQr = boxQr;
        _images = images;
    }

    // ─── FarmingArea ────────────────────────────────────────────────────────

    public async Task<ApiResponse<PagedResult<FarmingAreaDto>>> GetAreasAsync(
        FarmingAreaFilter filter, CancellationToken ct = default)
    {
        var all = await _uow.FarmingAreas.GetAllAsync(ct);
        var q = all.AsEnumerable();

        if (filter.OwnerId.HasValue && filter.OwnerId.Value != Guid.Empty)
            q = q.Where(a => a.OwnerId == filter.OwnerId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = ParseFarmStatus(filter.Status);
            q = q.Where(a => a.Status == status);
        }
        else if (filter.IsActive.HasValue)
            q = q.Where(a => a.IsActive == filter.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(a =>
                a.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || a.Code.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (a.Location?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                || (a.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                || a.Address.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (a.Region?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var owners = (await _uow.Users.GetAllAsync(ct)).ToDictionary(u => u.Id);
        var list = q.OrderBy(a => a.Code).ThenBy(a => a.Name).ToList();
        var stats = await BuildAreaStatsAsync(list.Select(a => a.Id), ct);
        return ApiResponse<PagedResult<FarmingAreaDto>>.Ok(
            Page(list, filter.Page, filter.PageSize,
                a => MapArea(a, owners.GetValueOrDefault(a.OwnerId)?.FullName, stats.GetValueOrDefault(a.Id))));
    }

    public async Task<ApiResponse<FarmingAreaDto>> GetAreaByIdAsync(Guid id, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);
        var owner = await _uow.Users.GetByIdAsync(area.OwnerId, ct);
        var stats = await BuildAreaStatsAsync(new[] { area.Id }, ct);
        return ApiResponse<FarmingAreaDto>.Ok(
            MapArea(area, owner?.FullName, stats.GetValueOrDefault(area.Id)));
    }

    public async Task<ApiResponse<NextFarmCodeDto>> GetNextAreaCodeAsync(CancellationToken ct = default)
        => ApiResponse<NextFarmCodeDto>.Ok(new NextFarmCodeDto(await PeekNextFarmCodeAsync(ct)));

    public async Task<ApiResponse<FarmingAreaDto>> CreateAreaAsync(
        CreateFarmingAreaRequest req, Guid ownerUserId, CancellationToken ct = default)
    {
        if (ownerUserId == Guid.Empty)
            throw AppException.BadRequest("Owner (logged-in user) is required to create an area.");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.AreaSquareMeters is < 0)
            throw AppException.BadRequest("AreaSquareMeters must be >= 0.");

        var owner = await _uow.Users.GetByIdAsync(ownerUserId, ct)
            ?? throw AppException.NotFound("Owner user");
        if (!owner.IsActive)
            throw AppException.BadRequest("Owner user is inactive.");

        var status = ParseFarmStatus(req.Status, FarmStatus.Active);
        var area = new FarmingArea
        {
            OwnerId = owner.Id,
            Code = await AllocateAreaCodeAsync(ct),
            Name = req.Name.Trim(),
            Location = NormalizeOptional(req.Location),
            Address = NormalizeOptional(req.Address) ?? "",
            Region = NormalizeOptional(req.Region),
            AreaSquareMeters = req.AreaSquareMeters,
            EstablishedAt = NormalizeDate(req.EstablishedAt),
            Description = NormalizeOptional(req.Description),
            AvatarUrl = NormalizeOptional(req.AvatarUrl),
            Latitude = NormalizeCoord(req.Latitude, -90, 90, "Latitude"),
            Longitude = NormalizeCoord(req.Longitude, -180, 180, "Longitude"),
        };
        ApplyStatus(area, status);
        await _uow.FarmingAreas.AddAsync(area, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area, owner.FullName), "Created.");
    }

    public async Task<ApiResponse<FarmingAreaDto>> UpdateAreaAsync(Guid id, UpdateFarmingAreaRequest req, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.AreaSquareMeters is < 0)
            throw AppException.BadRequest("AreaSquareMeters must be >= 0.");

        area.Name = req.Name.Trim();
        if (req.Location is not null)
            area.Location = NormalizeOptional(req.Location);
        if (req.AreaSquareMeters is not null)
            area.AreaSquareMeters = req.AreaSquareMeters;
        if (req.Description is not null)
            area.Description = NormalizeOptional(req.Description);
        if (req.Address is not null)
            area.Address = req.Address.Trim();
        if (req.Region is not null)
            area.Region = NormalizeOptional(req.Region);
        if (req.EstablishedAt is not null)
            area.EstablishedAt = NormalizeDate(req.EstablishedAt);
        if (req.AvatarUrl is not null)
            area.AvatarUrl = NormalizeOptional(req.AvatarUrl);
        if (req.Latitude is not null)
            area.Latitude = NormalizeCoord(req.Latitude, -90, 90, "Latitude");
        if (req.Longitude is not null)
            area.Longitude = NormalizeCoord(req.Longitude, -180, 180, "Longitude");

        if (!string.IsNullOrWhiteSpace(req.Status))
            ApplyStatus(area, ParseFarmStatus(req.Status));
        else if (req.IsActive is bool active)
            ApplyStatus(area, active ? FarmStatus.Active : FarmStatus.Closed);

        _uow.FarmingAreas.Update(area);
        await _uow.SaveChangesAsync(ct);
        var owner = await _uow.Users.GetByIdAsync(area.OwnerId, ct);
        var stats = await BuildAreaStatsAsync(new[] { area.Id }, ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area, owner?.FullName, stats.GetValueOrDefault(area.Id)));
    }

    public async Task<ApiResponse<FarmingAreaDto>> UpdateAreaMapAsync(
        Guid id, UpdateAreaMapRequest req, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);

        var coords = new[] { req.MapX1, req.MapY1, req.MapX2, req.MapY2 };
        var provided = coords.Count(c => c is not null);
        if (provided is not (0 or 4))
            throw AppException.BadRequest("MapX1, MapY1, MapX2, MapY2 must be sent together (all or none).");
        if (provided == 4)
        {
            foreach (var c in coords)
                ValidateMapRatio(c, "Map bounds");
            if (req.MapX2 <= req.MapX1 || req.MapY2 <= req.MapY1)
                throw AppException.BadRequest("Map bounds require MapX2 > MapX1 and MapY2 > MapY1.");
        }

        if (req.MapImageUrl is not null)
            area.MapImageUrl = NormalizeOptional(req.MapImageUrl);

        area.MapX1 = req.MapX1;
        area.MapY1 = req.MapY1;
        area.MapX2 = req.MapX2;
        area.MapY2 = req.MapY2;

        _uow.FarmingAreas.Update(area);
        await _uow.SaveChangesAsync(ct);
        var owner = await _uow.Users.GetByIdAsync(area.OwnerId, ct);
        var stats = await BuildAreaStatsAsync(new[] { area.Id }, ct);
        return ApiResponse<FarmingAreaDto>.Ok(
            MapArea(area, owner?.FullName, stats.GetValueOrDefault(area.Id)), "Map layout updated.");
    }

    /// <summary>Toạ độ bản đồ là tỉ lệ 0–1 theo kích thước ảnh.</summary>
    private static void ValidateMapRatio(decimal? value, string field)
    {
        if (value is < 0 or > 1)
            throw AppException.BadRequest($"{field} must be within 0..1 (ratio of the map image).");
    }

    private static void ValidateMapPoint(UpdateMapPointRequest req)
    {
        if ((req.MapX is null) != (req.MapY is null))
            throw AppException.BadRequest("MapX and MapY must be sent together (both or none).");
        ValidateMapRatio(req.MapX, "MapX");
        ValidateMapRatio(req.MapY, "MapY");
    }

    public async Task<ApiResponse<FarmAvatarDto>> UploadAvatarAsync(
        Guid? areaId, Stream data, string fileName, string contentType, Guid? uploadedBy, CancellationToken ct = default)
    {
        if (data is null)
            throw AppException.BadRequest("File stream is required.");
        if (string.IsNullOrWhiteSpace(fileName))
            throw AppException.BadRequest("File name is required.");

        var ext = Path.GetExtension(fileName);
        var typeOk = !string.IsNullOrWhiteSpace(contentType) && AvatarContentTypes.Contains(contentType);
        var extOk = !string.IsNullOrWhiteSpace(ext) && AvatarExtensions.Contains(ext);
        if (!typeOk && !extOk)
            throw AppException.BadRequest("Avatar must be jpeg, png, webp, or gif.");
        if (data.CanSeek && data.Length > MaxAvatarBytes)
            throw AppException.BadRequest("Avatar must be <= 5 MB.");

        FarmingArea? area = null;
        if (areaId is Guid id && id != Guid.Empty)
            area = await RequireAreaAsync(id, requireActive: false, ct);

        var folder = area is null
            ? MediaFolderPath.Join(MediaFolderPath.PendingSegment, MediaFolderPath.AreasLeaf)
            : MediaFolderPath.Join(area.Code, MediaFolderPath.AreasLeaf);
        var uploaded = await _images.UploadAsync(data, fileName, contentType, folder, ct);
        var url = uploaded.ShareLink ?? uploaded.WebContentLink ?? uploaded.WebViewLink
            ?? throw AppException.BadRequest("Upload succeeded but no public URL was returned.");

        var asset = new MediaAsset
        {
            Category = "image",
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
            SizeBytes = uploaded.SizeBytes,
            Provider = _images.ProviderName,
            StorageKey = uploaded.StorageKey,
            WebViewLink = uploaded.WebViewLink,
            WebContentLink = uploaded.WebContentLink,
            ShareLink = url,
            IsShared = true,
            UploadedBy = uploadedBy,
            RelatedEntityType = area is null ? "farm-pending" : "farm",
            RelatedEntityId = area?.Id,
            Notes = "farm-avatar"
        };
        await _uow.MediaAssets.AddAsync(asset, ct);

        if (area is not null)
        {
            area.AvatarUrl = url;
            _uow.FarmingAreas.Update(area);
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmAvatarDto>.Ok(
            new FarmAvatarDto(url, uploaded.StorageKey, fileName, _images.ProviderName, uploaded.SizeBytes),
            "Uploaded.");
    }

    /// <summary>
    /// Xoá khu.
    /// Mặc định: chỉ xoá được khi khu đã hết hàng (giữ nguyên hành vi cũ).
    /// cascade=true: xoá luôn cả cây con (cua → hộp → hàng) trong 1 transaction,
    /// và dọn các tham chiếu mềm còn trỏ tới chúng.
    /// </summary>
    public async Task<ApiResponse> DeleteAreaAsync(
        Guid id, bool cascade = false, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);

        if (!cascade)
        {
            var hasRows = await _uow.FarmingRows.AnyAsync(r => r.FarmingAreaId == id, ct);
            if (hasRows)
                throw AppException.Conflict("Cannot delete area that still has rows. Delete rows first.");

        _uow.FarmingAreas.Remove(area);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
        }

        // ── Cascade: gom khu → hàng → hộp → cua ────────────────────────────────
        var rows = (await _uow.FarmingRows.FindAsync(r => r.FarmingAreaId == id, ct)).ToList();
        var rowIds = rows.Select(r => r.Id).ToList();

        var boxes = rowIds.Count == 0
            ? new List<Box>()
            : (await _uow.Boxes.FindAsync(b => rowIds.Contains(b.FarmingRowId), ct)).ToList();
        var boxIds = boxes.Select(b => b.Id).ToList();

        var crabs = boxIds.Count == 0
            ? new List<Crab>()
            : (await _uow.Crabs.FindAsync(
                c => c.BoxId != null && boxIds.Contains(c.BoxId.Value), ct)).ToList();
        var crabIds = crabs.Select(c => c.Id).ToList();

        await RemoveDependentsAsync(crabIds, boxIds, ct);

        foreach (var crab in crabs) _uow.Crabs.Remove(crab);
        foreach (var box in boxes) _uow.Boxes.Remove(box);
        foreach (var row in rows) _uow.FarmingRows.Remove(row);
        _uow.FarmingAreas.Remove(area);

            await ClearOperationRefsAsync(boxIds, crabIds, ct);

        // Một SaveChanges duy nhất => gói trong 1 transaction: lỗi giữa đường thì
        // rollback, không để lại khu bị xoá dở.
        await _uow.SaveChangesAsync(ct);

        return ApiResponse.Ok(
            $"Deleted area '{area.Code}': {rowIds.Count} rows, {boxIds.Count} boxes, {crabIds.Count} crabs.");
    }

    // ─── FarmingRow ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<PagedResult<FarmingRowDto>>> GetRowsAsync(
        FarmingRowFilter filter, CancellationToken ct = default)
    {
        var all = await _uow.FarmingRows.GetAllAsync(ct);
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);
        var q = all.AsEnumerable();

        if (filter.FarmingAreaId.HasValue)
            q = q.Where(r => r.FarmingAreaId == filter.FarmingAreaId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = ParseFarmStatus(filter.Status);
            q = q.Where(r => r.Status == status);
        }
        else if (filter.IsActive.HasValue)
            q = q.Where(r => r.IsActive == filter.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(r =>
                r.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || r.Code.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (r.Location?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                || (areas.TryGetValue(r.FarmingAreaId, out var area)
                    && (area.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                        || area.Code.Contains(s, StringComparison.OrdinalIgnoreCase))));
        }

        var list = q.OrderBy(r => r.SortOrder).ThenBy(r => r.Code).ThenBy(r => r.Name).ToList();
        var stats = await BuildRowStatsAsync(list.Select(r => r.Id), ct);
        return ApiResponse<PagedResult<FarmingRowDto>>.Ok(
            Page(list, filter.Page, filter.PageSize,
                r => MapRow(r, areas.GetValueOrDefault(r.FarmingAreaId), stats.GetValueOrDefault(r.Id))));
    }

    public async Task<ApiResponse<FarmingRowDto>> GetRowByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await RequireRowAsync(id, requireActive: false, ct);
        var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
        var stats = await BuildRowStatsAsync(new[] { row.Id }, ct);
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row, area, stats.GetValueOrDefault(row.Id)));
    }

    public async Task<ApiResponse<NextRowCodeDto>> GetNextRowCodeAsync(CancellationToken ct = default)
        => ApiResponse<NextRowCodeDto>.Ok(new NextRowCodeDto(await PeekNextRowCodeAsync(ct)));

    public async Task<ApiResponse<FarmingRowDto>> CreateRowAsync(CreateFarmingRowRequest req, CancellationToken ct = default)
    {
        if (req.FarmingAreaId == Guid.Empty)
            throw AppException.BadRequest("FarmingAreaId is required — row must belong to an area.");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.Capacity < 0)
            throw AppException.BadRequest("Capacity must be >= 0 (0 = unlimited).");
        if (req.Capacity > 500)
            throw AppException.BadRequest("Capacity cannot exceed 500 boxes.");
        if (req.SortOrder is < 0)
            throw AppException.BadRequest("SortOrder must be >= 0.");

        var area = await RequireAreaAsync(req.FarmingAreaId, requireActive: true, ct);
        var status = ParseFarmStatus(req.Status, FarmStatus.Active);
        var name = req.Name.Trim();
        var sameArea = await _uow.FarmingRows.FindAsync(r => r.FarmingAreaId == area.Id, ct);
        if (sameArea.Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw AppException.BadRequest("Tên dãy đã tồn tại trong khu vực này.");

        var row = new FarmingRow
        {
            FarmingAreaId = area.Id,
            Code = await AllocateRowCodeAsync(ct),
            Name = name,
            Location = NormalizeOptional(req.Location),
            Description = NormalizeOptional(req.Description),
            Capacity = req.Capacity,
            SortOrder = req.SortOrder ?? 1
        };
        ApplyStatus(row, status);
        await _uow.FarmingRows.AddAsync(row, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row, area), "Created.");
    }

    public async Task<ApiResponse<FarmingRowDto>> UpdateRowAsync(Guid id, UpdateFarmingRowRequest req, CancellationToken ct = default)
    {
        var row = await RequireRowAsync(id, requireActive: false, ct);
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.Capacity is < 0)
            throw AppException.BadRequest("Capacity must be >= 0.");
        if (req.SortOrder is < 0)
            throw AppException.BadRequest("SortOrder must be >= 0.");

        if (req.Capacity is int cap && cap > 0)
        {
            var boxCount = (await _uow.Boxes.FindAsync(b => b.FarmingRowId == id, ct)).Count();
            if (cap < boxCount)
                throw AppException.BadRequest($"Capacity ({cap}) cannot be less than current box count ({boxCount}).");
        }

        var newName = req.Name.Trim();
        var sameArea = await _uow.FarmingRows.FindAsync(r => r.FarmingAreaId == row.FarmingAreaId, ct);
        if (sameArea.Any(r => r.Id != id && string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw AppException.BadRequest("Tên dãy đã tồn tại trong khu vực này.");

        row.Name = newName;
        if (req.Location is not null)
            row.Location = NormalizeOptional(req.Location);
        if (req.Description is not null)
            row.Description = NormalizeOptional(req.Description);
        if (req.Capacity is int capacity)
            row.Capacity = capacity;
        if (req.SortOrder is int sort)
            row.SortOrder = sort;

        if (!string.IsNullOrWhiteSpace(req.Status))
            ApplyStatus(row, ParseFarmStatus(req.Status));
        else if (req.IsActive is bool active)
            ApplyStatus(row, active ? FarmStatus.Active : FarmStatus.Closed);

        _uow.FarmingRows.Update(row);
        await _uow.SaveChangesAsync(ct);

        var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
        var stats = await BuildRowStatsAsync(new[] { row.Id }, ct);
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row, area, stats.GetValueOrDefault(row.Id)));
    }

    public async Task<ApiResponse<FarmingRowDto>> UpdateRowMapAsync(
        Guid id, UpdateMapPointRequest req, CancellationToken ct = default)
    {
        var row = await RequireRowAsync(id, requireActive: false, ct);
        ValidateMapPoint(req);

        row.MapX = req.MapX;
        row.MapY = req.MapY;
        _uow.FarmingRows.Update(row);
        await _uow.SaveChangesAsync(ct);

        var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
        var stats = await BuildRowStatsAsync(new[] { row.Id }, ct);
        return ApiResponse<FarmingRowDto>.Ok(
            MapRow(row, area, stats.GetValueOrDefault(row.Id)), "Map position updated.");
    }

    public async Task<ApiResponse> DeleteRowAsync(Guid id, CancellationToken ct = default)
    {
        var row = await RequireRowAsync(id, requireActive: false, ct);

        var hasBoxes = await _uow.Boxes.AnyAsync(b => b.FarmingRowId == id, ct);
        if (hasBoxes)
            throw AppException.Conflict("Cannot delete row that still has boxes. Delete boxes first.");

        _uow.FarmingRows.Remove(row);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
    }

    // ─── Box ────────────────────────────────────────────────────────────────

    public async Task<ApiResponse<PagedResult<BoxDto>>> GetBoxesAsync(BoxFilter filter, CancellationToken ct = default)
    {
        var ctx = await LoadBoxContextAsync(ct);
        var q = ctx.Boxes.AsEnumerable();

        if (filter.FarmingRowId.HasValue)
            q = q.Where(b => b.FarmingRowId == filter.FarmingRowId.Value);

        if (filter.FarmingAreaId.HasValue)
        {
            var rowIds = ctx.Rows.Values
                .Where(r => r.FarmingAreaId == filter.FarmingAreaId.Value)
                .Select(r => r.Id).ToHashSet();
            q = q.Where(b => rowIds.Contains(b.FarmingRowId));
        }

        if (filter.IsOccupied.HasValue)
            q = q.Where(b => b.IsOccupied == filter.IsOccupied.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            q = q.Where(b => string.Equals(b.Status, filter.Status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter.Code))
        {
            var code = filter.Code.Trim();
            q = q.Where(b => BoxMatchesSearch(b, ctx, code));
        }

        return ApiResponse<PagedResult<BoxDto>>.Ok(
            Page(q.OrderBy(b => b.Code), filter.Page, filter.PageSize, b => MapBox(b, ctx)));
    }

    public async Task<ApiResponse<BoxDto>> GetBoxByIdAsync(Guid id, CancellationToken ct = default)
    {
        var box = await RequireBoxAsync(id, ct);
        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<BoxDto>.Ok(MapBox(box, ctx));
    }

    public async Task<ApiResponse<FarmAvailabilityDto>> GetAvailabilityAsync(
        Guid? farmingAreaId, Guid? farmingRowId, CancellationToken ct = default)
    {
        var snapshot = await BuildAvailabilityAsync(farmingAreaId, farmingRowId, ct);
        return ApiResponse<FarmAvailabilityDto>.Ok(snapshot);
    }

    public async Task<ApiResponse<BoxDto>> CreateBoxAsync(CreateBoxRequest req, CancellationToken ct = default)
    {
        if (req.FarmingRowId == Guid.Empty)
            throw AppException.BadRequest("FarmingRowId is required — box must belong to a row.");

        var row = await RequireRowAsync(req.FarmingRowId, requireActive: true, ct);

        // Auto-fill Area from Row (parent). If client sends AreaId, it must match.
        if (req.FarmingAreaId is Guid sentArea && sentArea != Guid.Empty && sentArea != row.FarmingAreaId)
            throw AppException.BadRequest(
                $"FarmingAreaId does not match row '{row.Name}'. Omit farmingAreaId to auto-fill.");

        var area = await RequireAreaAsync(row.FarmingAreaId, requireActive: true, ct);

        if (row.Capacity > 0)
        {
            var boxCount = (await _uow.Boxes.FindAsync(b => b.FarmingRowId == row.Id, ct)).Count();
            if (boxCount >= row.Capacity)
                throw AppException.Conflict(
                    $"Row '{row.Name}' is full (capacity {row.Capacity}). Cannot add more boxes.");
        }

        string code;
        if (string.IsNullOrWhiteSpace(req.Code))
        {
            code = await NextGlobalBoxCodeAsync(ct);
        }
        else
        {
            code = req.Code.Trim();
            if (await _uow.Boxes.AnyAsync(b => b.Code == code, ct))
                throw AppException.Conflict($"Box code '{code}' already exists.");
        }

        var box = new Box { FarmingRowId = row.Id, Code = code, Status = "empty" };
        await _uow.Boxes.AddAsync(box, ct);
        await _uow.SaveChangesAsync(ct);
        await _boxQr.EnsureBoxQrAsync(box.Id, ct);

        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<BoxDto>.Ok(MapBox(box, ctx), $"Created under {area.Name} / {row.Name}.");
    }

    public async Task<ApiResponse<IReadOnlyList<BoxDto>>> CreateBoxesBulkAsync(
        Guid rowId, int quantity, CancellationToken ct = default)
    {
        if (quantity < 1)
            throw AppException.BadRequest("Quantity must be >= 1.");
        if (quantity > 100)
            throw AppException.BadRequest("Quantity cannot exceed 100 boxes.");

        var row = await RequireRowAsync(rowId, requireActive: true, ct);
        await RequireAreaAsync(row.FarmingAreaId, requireActive: true, ct);

        var boxCount = (await _uow.Boxes.FindAsync(b => b.FarmingRowId == row.Id, ct)).Count();
        if (row.Capacity > 0)
        {
            var remaining = row.Capacity - boxCount;
            if (remaining <= 0)
                throw AppException.Conflict(
                    $"Row '{row.Name}' is full (capacity {row.Capacity}). Cannot add more boxes.");
            if (quantity > remaining)
                throw AppException.Conflict(
                    $"Row '{row.Name}' only has remaining capacity for {remaining} box(es) ({boxCount}/{row.Capacity}).");
        }

        var codes = await AllocateBoxCodesAsync(quantity, ct);
        var created = new List<Box>();
        foreach (var code in codes)
        {
            var box = new Box { FarmingRowId = row.Id, Code = code, Status = "empty" };
            await _uow.Boxes.AddAsync(box, ct);
            created.Add(box);
        }
        await _uow.SaveChangesAsync(ct);
        foreach (var box in created)
            await _boxQr.EnsureBoxQrAsync(box.Id, ct);

        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<IReadOnlyList<BoxDto>>.Ok(
            created.Select(b => MapBox(b, ctx)).ToList(),
            $"Created {created.Count} box(es) under {row.Name}.");
    }

    /// <summary>Next farm-wide box code: BOX-0001, BOX-0002, …</summary>
    private async Task<string> NextGlobalBoxCodeAsync(CancellationToken ct)
    {
        var n = await NextGlobalBoxNumberAsync(ct);
        return $"BOX-{n:D4}";
    }

    private async Task<int> MaxGlobalBoxNumberAsync(CancellationToken ct)
    {
        const string prefix = "BOX-";
        var boxes = await _uow.Boxes.GetAllAsync(ct);
        var max = 0;
        foreach (var b in boxes)
        {
            if (string.IsNullOrWhiteSpace(b.Code)) continue;
            if (!b.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = b.Code[prefix.Length..];
            if (int.TryParse(suffix, out var n) && n > max)
                max = n;
        }
        return max;
    }

    /// <summary>Next free BOX-#### number (first unused, then max+1).</summary>
    private async Task<int> NextGlobalBoxNumberAsync(CancellationToken ct)
    {
        const string prefix = "BOX-";
        var boxes = await _uow.Boxes.GetAllAsync(ct);
        var used = new HashSet<int>();
        var max = 0;
        foreach (var b in boxes)
        {
            if (string.IsNullOrWhiteSpace(b.Code)) continue;
            if (!b.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = b.Code[prefix.Length..];
            if (!int.TryParse(suffix, out var n)) continue;
            used.Add(n);
            if (n > max) max = n;
        }

        for (var i = 1; i <= max + 1; i++)
        {
            if (!used.Contains(i))
                return i;
        }

        return max + 1;
    }

    /// <summary>Allocate N unused BOX-#### codes (skips gaps / deleted numbers).</summary>
    private async Task<List<string>> AllocateBoxCodesAsync(int count, CancellationToken ct)
    {
        const string prefix = "BOX-";
        var boxes = await _uow.Boxes.GetAllAsync(ct);
        var used = new HashSet<int>();
        var max = 0;
        foreach (var b in boxes)
        {
            if (string.IsNullOrWhiteSpace(b.Code)) continue;
            if (!b.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = b.Code[prefix.Length..];
            if (!int.TryParse(suffix, out var n)) continue;
            used.Add(n);
            if (n > max) max = n;
        }

        var codes = new List<string>(count);
        for (var i = 1; codes.Count < count; i++)
        {
            if (used.Contains(i)) continue;
            used.Add(i);
            codes.Add($"{prefix}{i:D4}");
        }
        return codes;
    }

    public async Task<ApiResponse<BoxDto>> UpdateBoxAsync(Guid id, UpdateBoxRequest req, CancellationToken ct = default)
    {
        var box = await RequireBoxAsync(id, ct);
        if (string.IsNullOrWhiteSpace(req.Code))
            throw AppException.BadRequest("Code is required.");

        var code = req.Code.Trim();
        if (await _uow.Boxes.AnyAsync(b => b.Code == code && b.Id != id, ct))
            throw AppException.Conflict($"Box code '{code}' already exists.");

        box.Code = code;
        box.Status = req.Status;
        box.IsOccupied = req.IsOccupied;
        _uow.Boxes.Update(box);
        await _uow.SaveChangesAsync(ct);

        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<BoxDto>.Ok(MapBox(box, ctx));
    }

    public async Task<ApiResponse<BoxDto>> UpdateBoxStatusAsync(
        Guid boxId, UpdateBoxStatusRequest req, CancellationToken ct = default)
    {
        var box = await RequireBoxAsync(boxId, ct);
        if (string.IsNullOrWhiteSpace(req.Status))
            throw AppException.BadRequest("Status is required.");
        if (!BoxStatuses.IsValid(req.Status))
            throw AppException.BadRequest(
                $"Invalid box status '{req.Status}'. Allowed: {string.Join(", ", BoxStatuses.All)}");

        var normalized = BoxStatuses.Normalize(req.Status);
        var oldStatus = box.Status;
        var oldOcc = box.IsOccupied;

        box.Status = normalized;
        box.IsOccupied = req.IsOccupied;
        _uow.Boxes.Update(box);

        if (!string.Equals(oldStatus, normalized, StringComparison.OrdinalIgnoreCase) || oldOcc != req.IsOccupied)
        {
            await _uow.BoxStatusHistories.AddAsync(new BoxStatusHistory
            {
                BoxId = box.Id,
                OldStatus = oldStatus,
                NewStatus = normalized,
                OldIsOccupied = oldOcc,
                NewIsOccupied = req.IsOccupied,
                ChangedAt = DateTime.UtcNow,
                Reason = "Manual status update"
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);

        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<BoxDto>.Ok(MapBox(box, ctx));
    }

    public async Task<ApiResponse<BoxDto>> UpdateBoxMapAsync(
        Guid boxId, UpdateMapPointRequest req, CancellationToken ct = default)
    {
        var box = await RequireBoxAsync(boxId, ct);
        ValidateMapPoint(req);

        box.MapX = req.MapX;
        box.MapY = req.MapY;
        _uow.Boxes.Update(box);
        await _uow.SaveChangesAsync(ct);

        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<BoxDto>.Ok(MapBox(box, ctx), "Map position updated.");
    }

    public async Task<ApiResponse> DeleteBoxAsync(Guid id, CancellationToken ct = default)
    {
        var box = await RequireBoxAsync(id, ct);
        var crabsInBox = await _uow.Crabs.Query()
            .Include(c => c.BoxAllocations)
            .Where(c => c.BoxAllocations.Any(a => a.BoxId == id && a.EndTime == null))
            .ToListAsync(ct);
        var hasCrabs = crabsInBox.Any(IsCrabAlive); if (hasCrabs)
            throw AppException.Conflict("Cannot delete box with live crabs. Move/harvest first.");

        var openAlloc = await _uow.CrabBoxAllocations.AnyAsync(a => a.BoxId == id && a.EndTime == null, ct);
        if (openAlloc)
            throw AppException.Conflict("Box still has an open allocation. Move the crab first.");

        var qrs = await _uow.QrCodes.FindAsync(q => q.BoxId == id, ct);
        foreach (var qr in qrs)
            _uow.QrCodes.Remove(qr);

        _uow.Boxes.Remove(box);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
    }

    // ─── Crab ───────────────────────────────────────────────────────────────

    public async Task<ApiResponse<PagedResult<CrabDto>>> GetCrabsAsync(int page, int pageSize, Guid? farmingAreaId = null, CancellationToken ct = default)
    {
        var all = await _uow.Crabs.Query()
                    .Include(c => c.BoxAllocations)
                    .Include(c => c.MoltingRecords)
                    .ToListAsync(ct);
        var ctx = await LoadBoxContextAsync(ct);
        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            all = all.Where(c =>
            {
                var boxId = LastKnownBoxId(c, ctx);
                var box = ctx.Boxes.FirstOrDefault(b => b.Id == boxId);
                if (box is null) return false;
                return ctx.Rows.TryGetValue(box.FarmingRowId, out var row)
                    && row.FarmingAreaId == areaId;
            }).ToList();
        }
        int? size = pageSize <= 0 ? null : pageSize;
        return ApiResponse<PagedResult<CrabDto>>.Ok(
            Page(all.OrderByDescending(c => c.CreatedAt), page, size, c => MapCrab(c, ctx)));
    }

    public async Task<ApiResponse<NextCrabCodeDto>> GetNextCrabCodeAsync(CancellationToken ct = default)
    {
        var code = await CrabCodeAllocator.PeekNextAsync(_uow.Crabs, ct);
        return ApiResponse<NextCrabCodeDto>.Ok(new NextCrabCodeDto(code, $"QR-{code}"));
    }

    public async Task<ApiResponse<CrabDto>> GetCrabByIdAsync(Guid id, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.Query()
            .Include(c => c.BoxAllocations)
            .Include(c => c.MoltingRecords)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Crab");
        var ctx = await LoadBoxContextAsync(ct);
        var lot = crab.CrabLotId != Guid.Empty
            ? await _uow.CrabLots.GetByIdAsync(crab.CrabLotId, ct)
            : null;
        var latestAi = (await _uow.CrabAiAnalyses.FindAsync(a => a.CrabId == id, ct))
            .OrderByDescending(a => a.AnalyzedAt)
            .FirstOrDefault();
        return ApiResponse<CrabDto>.Ok(MapCrab(crab, ctx, lot, latestAi));
    }

    public async Task<ApiResponse<CrabProfileDto>> GetCrabProfileAsync(Guid id, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.Query()
            .Include(c => c.BoxAllocations)
            .Include(c => c.MoltingRecords)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Crab");
        var ctx = await LoadBoxContextAsync(ct);
        var lot = crab.CrabLotId != Guid.Empty
            ? await _uow.CrabLots.GetByIdAsync(crab.CrabLotId, ct)
            : null;
        var analyses = (await _uow.CrabAiAnalyses.FindAsync(a => a.CrabId == id, ct))
            .OrderByDescending(a => a.AnalyzedAt)
            .ToList();
        var latestAi = analyses.FirstOrDefault();
        var dto = MapCrab(crab, ctx, lot, latestAi);

        var box = ctx.Boxes.FirstOrDefault(b => b.Id == dto.BoxId);
        ctx.Rows.TryGetValue(box?.FarmingRowId ?? dto.FarmingRowId, out var row);
        ctx.Areas.TryGetValue(row?.FarmingAreaId ?? dto.FarmingAreaId, out var area);

        var recs = (await _uow.AiRecommendations.FindAsync(
                r => r.RelatedEntityId == id && r.RelatedEntityType == "Crab", ct))
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        var recommendation = FirstNonEmpty(
            latestAi?.AnomalyNote,
            recs.FirstOrDefault()?.Recommendation,
            RecommendFromPrediction(dto.AiPrediction ?? latestAi?.Prediction));

        var media = dto.ImageUrls.ToList();
        foreach (var url in analyses.Select(a => a.MediaUrl).Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            if (!media.Contains(url!)) media.Add(url!);
        }
        var assets = (await _uow.MediaAssets.FindAsync(m => m.CrabId == id, ct)).ToList();
        foreach (var link in assets.Select(a => a.ShareLink ?? a.WebContentLink ?? a.WebViewLink)
                     .Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            if (!media.Contains(link!)) media.Add(link!);
        }

        var timeline = await BuildCrabTimelineAsync(crab, lot, analyses, ct);
        var alerts = BuildCrabProfileAlerts(crab, latestAi, timeline);

        return ApiResponse<CrabProfileDto>.Ok(new CrabProfileDto(
            dto,
            new CrabProfileLocationDto(
                dto.FarmingAreaId, dto.FarmingRowId, dto.BoxId,
                area?.Name ?? dto.AreaName, area?.Code ?? dto.AreaCode,
                row?.Name ?? dto.RowName, row?.Code ?? dto.RowCode,
                box?.Code ?? dto.BoxCode),
            new CrabProfileLotDto(
                lot?.Id ?? crab.CrabLotId,
                lot?.LotCode ?? dto.LotCode ?? "",
                lot?.Name ?? dto.LotName,
                lot?.ImportDate ?? dto.ImportDate),
            new CrabProfileAiDto(
                dto.AiPrediction ?? latestAi?.Prediction,
                dto.AiConfidence ?? latestAi?.Confidence,
                latestAi?.AnalyzedAt ?? dto.AiAnalyzedAt,
                recommendation,
                latestAi?.ActivityLevel,
                latestAi?.MediaUrl),
            media,
            timeline,
            alerts));
    }

    public async Task<ApiResponse<CrabDto>> CreateCrabAsync(CreateCrabRequest req, CancellationToken ct = default)
    {
        Box box;

        if (req.AutoAssignEmptyBox)
        {
            // Need Row and/or Area to scope empty-box search; Area auto-fills from Row.
            Guid? rowId = req.FarmingRowId is Guid r && r != Guid.Empty ? r : null;
            Guid? areaId = req.FarmingAreaId is Guid a && a != Guid.Empty ? a : null;

            if (rowId.HasValue)
            {
                var scopedRow = await RequireRowAsync(rowId.Value, requireActive: true, ct);
                if (areaId.HasValue && areaId.Value != scopedRow.FarmingAreaId)
                    throw AppException.BadRequest("FarmingAreaId does not match FarmingRowId. Omit area to auto-fill.");
                areaId = scopedRow.FarmingAreaId;
            }

            if (!areaId.HasValue)
                throw AppException.BadRequest(
                    "With autoAssignEmptyBox=true provide farmingRowId and/or farmingAreaId.");

            _ = await RequireAreaAsync(areaId.Value, requireActive: true, ct);

            var empty = await ListEmptyBoxesAsync(areaId.Value, rowId, ct);
            box = empty.FirstOrDefault(b => !IsBoxUnusable(b.Status))
                ?? throw AppException.Conflict(
                    rowId.HasValue
                        ? "Không còn hộp trống phù hợp trong dãy này."
                        : "Không còn hộp trống phù hợp trong khu vực này.");
        }
        else
        {
            // Prefer lowest id: BoxId → auto Row + Area
            if (req.BoxId is null || req.BoxId == Guid.Empty)
                throw AppException.BadRequest(
                    "Provide boxId, or set autoAssignEmptyBox=true to pick next empty box.");

            box = await RequireBoxAsync(req.BoxId.Value, ct);
            if (IsBoxUnusable(box.Status))
                throw AppException.BadRequest($"Hộp '{box.Code}' không thể sử dụng.");
            var row = await RequireRowAsync(box.FarmingRowId, requireActive: true, ct);
            _ = await RequireAreaAsync(row.FarmingAreaId, requireActive: true, ct);

            if (req.FarmingRowId is Guid fr && fr != Guid.Empty && fr != row.Id)
                throw AppException.BadRequest("FarmingRowId does not match box. Omit farmingRowId to auto-fill.");
            if (req.FarmingAreaId is Guid fa && fa != Guid.Empty && fa != row.FarmingAreaId)
                throw AppException.BadRequest("FarmingAreaId does not match box. Omit farmingAreaId to auto-fill.");
        }

        var liveInBox = await _uow.Crabs.Query()
            .Include(c => c.BoxAllocations)
            .AnyAsync(c => c.BoxAllocations.Any(a => a.BoxId == box.Id && a.EndTime == null)
                           && (c.Status == CrabStatus.Alive
                               || c.Status == CrabStatus.Molting
                               || c.Status == CrabStatus.Quarantined), ct);
        if (liveInBox)
            throw AppException.Conflict($"Hộp '{box.Code}' vừa được sử dụng.");

        if (req.CrabLotId == Guid.Empty)
            throw AppException.BadRequest("CrabLotId is required — crab must belong to a lot.");

        _ = await _uow.CrabLots.GetByIdAsync(req.CrabLotId, ct)
            ?? throw AppException.NotFound("CrabLot");
        await EnsureLotCapacityAsync(req.CrabLotId, 1, ct);

        if (req.WeightGram is null or <= 0)
            throw AppException.BadRequest("WeightGram is required and must be > 0.");
        if (req.CarapaceWidthMm is null or <= 0)
            throw AppException.BadRequest("CarapaceWidthMm (bề rộng mai) is required and must be > 0.");
        if (req.CarapaceLengthMm is null or <= 0)
            throw AppException.BadRequest("CarapaceLengthMm (bề ngang mai) is required and must be > 0.");

        var code = await AllocateCrabCodeAsync(ct);
        var condition = string.IsNullOrWhiteSpace(req.Condition)
            ? CrabConditions.FromMoltingAndStatus(req.MoltingStage, CrabStatus.Alive)
            : CrabConditions.Parse(req.Condition);
        var status = CrabConditions.ToLifecycle(condition);
        var initialWeight = req.InitialWeightGram ?? req.WeightGram;

        var crab = new Crab
        {
            BoxId = box.Id,
            CrabLotId = req.CrabLotId,
            Code = code,
            QrCode = $"QR-{code}",
            Tag = string.IsNullOrWhiteSpace(req.Tag) ? code : req.Tag.Trim(),
            CrabType = string.IsNullOrWhiteSpace(req.CrabType) ? null : req.CrabType.Trim(),
            Gender = CrabConditions.ParseGender(req.Gender),
            WeightGram = req.WeightGram,
            InitialWeightGram = initialWeight,
            CarapaceWidthMm = req.CarapaceWidthMm,
            CarapaceLengthMm = req.CarapaceLengthMm,
            InitialCondition = string.IsNullOrWhiteSpace(req.InitialCondition)
                ? "Khỏe mạnh"
                : req.InitialCondition.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            MoltingStage = req.MoltingStage ?? "hard-shell",
            StockedAt = req.StockedAt ?? DateTime.UtcNow,
            Status = status,
            Condition = condition,
            ImageUrlsJson = JsonStringList.Serialize(req.ImageUrls)
        };
        await _uow.Crabs.AddAsync(crab, ct);

        await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
        {
            CrabId = crab.Id,
            NewCondition = condition,
            NewStatus = status,
            ChangedAt = DateTime.UtcNow,
            Source = req.AutoAssignEmptyBox ? "AUTO_ASSIGN" : "MANUAL_ASSIGN",
            Reason = "CRAB_CREATED"
        }, ct);
        if (initialWeight is decimal w)
        {
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = w,
                CarapaceWidthMm = req.CarapaceWidthMm,
                CarapaceLengthMm = req.CarapaceLengthMm,
                MeasuredAt = crab.StockedAt,
                Source = "INITIAL",
                Notes = "Đo lường ban đầu"
            }, ct);
        }

        await _uow.QrCodes.AddAsync(new QrCode
        {
            Code = crab.QrCode!,
            EntityType = "crab",
            CrabId = crab.Id,
            BoxId = box.Id,
            IsActive = true,
            Payload =
                $"{{\"type\":\"crab\",\"crabId\":\"{crab.Id}\",\"crabCode\":\"{code}\",\"crabsense\":\"CRABSENSE:CRAB:{code}\"}}"
        }, ct);

        await _uow.CrabBoxAllocations.AddAsync(new CrabBoxAllocation
        {
            CrabId = crab.Id,
            BoxId = box.Id,
            StartTime = DateTime.UtcNow,
            Notes = req.AutoAssignEmptyBox ? "Auto-assigned empty box" : "Initial placement"
        }, ct);

        box.IsOccupied = true;
        if (string.IsNullOrWhiteSpace(box.Status) || box.Status == "empty")
            box.Status = "active";
        _uow.Boxes.Update(box);

        await _uow.SaveChangesAsync(ct);

        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<CrabDto>.Ok(MapCrab(crab, ctx),
            req.AutoAssignEmptyBox ? $"Created (auto box {box.Code})." : $"Created in box {box.Code}.");
    }

    public async Task<ApiResponse<CreateCrabsBulkDto>> CreateCrabsBulkAsync(
        CreateCrabsBulkRequest req, CancellationToken ct = default)
    {
        var items = req.Items ?? Array.Empty<CreateCrabBulkItem>();
        if (items.Count == 0)
            throw AppException.BadRequest("Danh sách cua trống.");
        if (items.Count > 80)
            throw AppException.BadRequest("Mỗi lần tối đa 80 cá thể.");

        await EnsureLotCapacityAsync(req.CrabLotId, items.Count, ct);

        Guid? areaId = req.FarmingAreaId is Guid a && a != Guid.Empty ? a : null;
        Guid? rowId = req.FarmingRowId is Guid r && r != Guid.Empty ? r : null;
        if (rowId.HasValue)
        {
            var scopedRow = await RequireRowAsync(rowId.Value, requireActive: true, ct);
            if (areaId.HasValue && areaId.Value != scopedRow.FarmingAreaId)
                throw AppException.BadRequest("FarmingAreaId does not match FarmingRowId.");
            areaId = scopedRow.FarmingAreaId;
        }
        if (areaId.HasValue)
            _ = await RequireAreaAsync(areaId.Value, requireActive: true, ct);

        var reserved = new HashSet<Guid>();
        var assigned = new List<(CreateCrabBulkItem item, Box box)>(items.Count);
        foreach (var item in items)
        {
            if (item.WeightGram <= 0)
                throw AppException.BadRequest("Cân nặng phải > 0.");
            if (item.CarapaceWidthMm <= 0 || item.CarapaceLengthMm <= 0)
                throw AppException.BadRequest("Rộng mai và dài mai phải > 0.");

            Box box;
            if (!item.AutoAssign && item.TargetBoxId is Guid tid && tid != Guid.Empty)
            {
                box = await RequireBoxAsync(tid, ct);
                var boxRow = await RequireRowAsync(box.FarmingRowId, requireActive: true, ct);
                _ = await RequireAreaAsync(boxRow.FarmingAreaId, requireActive: true, ct);
                if (rowId.HasValue && box.FarmingRowId != rowId.Value)
                    throw AppException.BadRequest($"Hộp '{box.Code}' không thuộc dãy đã chọn.");
                if (areaId.HasValue && boxRow.FarmingAreaId != areaId.Value)
                    throw AppException.BadRequest($"Hộp '{box.Code}' không thuộc khu đã chọn.");
                if (IsBoxUnusable(box.Status))
                    throw AppException.BadRequest($"Hộp '{box.Code}' không thể sử dụng.");
                if (!reserved.Add(box.Id))
                    throw AppException.BadRequest($"Hộp '{box.Code}' bị chọn trùng.");
            }
            else
            {
                if (!areaId.HasValue)
                    throw AppException.BadRequest("Cần khu vực để tự gán hộp trống.");
                var empty = await ListEmptyBoxesAsync(areaId.Value, rowId, ct);
                box = empty.FirstOrDefault(b => !reserved.Contains(b.Id) && !IsBoxUnusable(b.Status))
                    ?? throw AppException.Conflict("Không còn hộp trống phù hợp.");
                reserved.Add(box.Id);
            }

            var liveInBox = await _uow.Crabs.Query()
                .Include(c => c.BoxAllocations)
                .AnyAsync(c => c.BoxAllocations.Any(al => al.BoxId == box.Id && al.EndTime == null)
                               && (c.Status == CrabStatus.Alive
                                   || c.Status == CrabStatus.Molting
                                   || c.Status == CrabStatus.Quarantined), ct);
            if (liveInBox)
                throw AppException.Conflict($"Hộp '{box.Code}' vừa được sử dụng. Vui lòng làm mới và chọn lại hộp.");

            assigned.Add((item, box));
        }

        var created = new List<Crab>(assigned.Count);
        var nextNo = await CrabCodeAllocator.NextNumberAsync(_uow.Crabs, ct);
        foreach (var (item, box) in assigned)
        {
            var single = new CreateCrabRequest(
                req.CrabLotId,
                BoxId: box.Id,
                FarmingRowId: req.FarmingRowId,
                FarmingAreaId: req.FarmingAreaId,
                WeightGram: item.WeightGram,
                ImageUrls: item.ImageUrls,
                StockedAt: req.StockedAt,
                CrabType: req.CrabType,
                Gender: item.Gender,
                InitialWeightGram: item.WeightGram,
                CarapaceWidthMm: item.CarapaceWidthMm,
                InitialCondition: req.InitialCondition,
                Notes: item.Note,
                Condition: req.Condition,
                CarapaceLengthMm: item.CarapaceLengthMm);
            var crab = await PersistNewCrabAsync(
                single, box, item.AutoAssign, ct, forcedCode: $"CRAB-{nextNo:D4}");
            nextNo++;
            created.Add(crab);
        }

        await _uow.SaveChangesAsync(ct);
        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<CreateCrabsBulkDto>.Ok(
            new CreateCrabsBulkDto(created.Select(c => MapCrab(c, ctx)).ToList(), created.Count),
            $"Đã tạo {created.Count} cá thể cua.");
    }

    public async Task<ApiResponse<CrabDto>> UpdateCrabAsync(Guid id, UpdateCrabRequest req, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        var oldCondition = crab.Condition;
        var oldStatus = crab.Status;
        var oldWeight = crab.WeightGram;
        var oldType = crab.CrabType;
        var oldGender = crab.Gender;
        var oldNotes = crab.Notes;
        var oldStage = crab.MoltingStage;

        if (req.WeightGram is decimal w) crab.WeightGram = w;
        if (req.MoltedAt is not null) crab.MoltedAt = req.MoltedAt;
        if (req.MoltingStage is not null) crab.MoltingStage = req.MoltingStage;
        if (req.Notes is not null) crab.Notes = req.Notes.Trim();
        if (req.CrabType is not null) crab.CrabType = req.CrabType.Trim();
        if (req.Gender is not null) crab.Gender = CrabConditions.ParseGender(req.Gender);
        if (req.CarapaceWidthMm is not null) crab.CarapaceWidthMm = req.CarapaceWidthMm;
        if (req.CarapaceLengthMm is not null) crab.CarapaceLengthMm = req.CarapaceLengthMm;
        if (req.ImageUrls is not null)
            crab.ImageUrlsJson = JsonStringList.Serialize(req.ImageUrls);

        if (!string.IsNullOrWhiteSpace(req.Condition))
            crab.Condition = CrabConditions.Parse(req.Condition, crab.Condition);
        else if (req.MoltingStage is not null)
            crab.Condition = CrabConditions.FromMoltingAndStatus(req.MoltingStage, crab.Status);

        if (req.IsAlive == false
            && crab.Condition is not CrabCondition.Harvested
            && crab.Condition is not CrabCondition.Sold)
            crab.Condition = CrabCondition.Dead;
        if (req.IsAlive == true && crab.Condition == CrabCondition.Dead)
            crab.Condition = CrabConditions.FromMoltingAndStatus(crab.MoltingStage, CrabStatus.Alive);

        crab.Status = CrabConditions.ToLifecycle(crab.Condition);
        _uow.Crabs.Update(crab);

        var profileChanged = oldCondition != crab.Condition
            || oldStatus != crab.Status
            || !string.Equals(oldType, crab.CrabType, StringComparison.Ordinal)
            || oldGender != crab.Gender
            || !string.Equals(oldNotes, crab.Notes, StringComparison.Ordinal)
            || !string.Equals(oldStage, crab.MoltingStage, StringComparison.Ordinal);
        if (profileChanged)
        {
            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldCondition = oldCondition,
                NewCondition = crab.Condition,
                OldStatus = oldStatus,
                NewStatus = crab.Status,
                ChangedAt = DateTime.UtcNow,
                Source = "manual",
                Reason = "CRAB_PROFILE_UPDATED"
                    + $"|health={ConditionVi(oldCondition, oldStatus)}→{ConditionVi(crab.Condition, crab.Status)}"
                    + $"|stage={oldStage}→{crab.MoltingStage}"
                    + $"|type={oldType}→{crab.CrabType}"
                    + $"|gender={oldGender}→{crab.Gender}"
            }, ct);
        }

        if (req.WeightGram is decimal nextWeight && oldWeight != nextWeight)
        {
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = nextWeight,
                CarapaceWidthMm = crab.CarapaceWidthMm,
                CarapaceLengthMm = crab.CarapaceLengthMm,
                Source = "manual"
            }, ct);
        }

        if (crab.Condition == CrabCondition.Harvested && oldCondition != CrabCondition.Harvested)
        {
            await _uow.CrabHarvestHistories.AddAsync(new CrabHarvestHistory
            {
                CrabId = crab.Id,
                HarvestedAt = DateTime.UtcNow,
                WeightGram = crab.WeightGram,
                Notes = "Cập nhật trạng thái thu hoạch"
            }, ct);
        }

        // If crab dies / harvest → free box if no other live crabs
        if (!IsCrabAlive(crab))
        {
            await ReleaseCrabFromBoxAsync(crab, "Crab no longer alive", ct);
        }

        await _uow.SaveChangesAsync(ct);
        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<CrabDto>.Ok(MapCrab(crab, ctx));
    }

    public async Task<ApiResponse> DeleteCrabAsync(Guid id, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        var alreadyInactive = !IsCrabAlive(crab);

        if (!alreadyInactive)
        {
            var oldCondition = crab.Condition;
            var oldStatus = crab.Status;
            crab.Status = CrabStatus.Dead;
            crab.Condition = CrabCondition.Dead;
            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldCondition = oldCondition,
                NewCondition = CrabCondition.Dead,
                OldStatus = oldStatus,
                NewStatus = CrabStatus.Dead,
                ChangedAt = DateTime.UtcNow,
                Source = "system",
                Reason = "Soft-delete"
            }, ct);
        }

        var boxId = CurrentBoxId(crab);
        await ReleaseCrabFromBoxAsync(crab, "Crab soft-deleted", ct);

        await _uow.SaveChangesAsync(ct);
        var box = boxId == Guid.Empty ? null : await _uow.Boxes.GetByIdAsync(boxId, ct);
        return ApiResponse.Ok(
            alreadyInactive
                ? $"Crab already inactive; box '{box?.Code}' freed if empty."
                : $"Crab soft-deleted (IsAlive=false); box '{box?.Code}' freed if empty.");
    }

    public async Task<ApiResponse<IReadOnlyList<CrabStatusHistoryDto>>> GetCrabStatusHistoryAsync(
        Guid crabId, CancellationToken ct = default)
    {
        _ = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        var rows = (await _uow.CrabStatusHistories.FindAsync(h => h.CrabId == crabId, ct))
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new CrabStatusHistoryDto(
                h.Id, h.CrabId,
                h.OldCondition?.ToString(), h.NewCondition.ToString(),
                h.OldStatus?.ToString(), h.NewStatus.ToString(),
                h.ChangedAt, h.Source, h.Reason))
            .ToList();
        return ApiResponse<IReadOnlyList<CrabStatusHistoryDto>>.Ok(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<CrabWeightHistoryDto>>> GetCrabWeightHistoryAsync(
        Guid crabId, CancellationToken ct = default)
    {
        _ = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        var rows = (await _uow.CrabWeightHistories.FindAsync(h => h.CrabId == crabId, ct))
            .OrderByDescending(h => h.MeasuredAt)
            .Select(MapWeight)
            .ToList();
        return ApiResponse<IReadOnlyList<CrabWeightHistoryDto>>.Ok(rows);
    }

    public async Task<ApiResponse<CrabWeightHistoryDto>> RecordCrabWeightAsync(
        Guid crabId, RecordCrabWeightRequest req, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        if (req.WeightGram <= 0)
            throw AppException.BadRequest("WeightGram must be greater than 0.");
        if (req.CarapaceWidthMm is <= 0)
            throw AppException.BadRequest("CarapaceWidthMm must be greater than 0.");
        if (req.CarapaceLengthMm is <= 0)
            throw AppException.BadRequest("CarapaceLengthMm must be greater than 0.");

        var at = req.MeasuredAt ?? DateTime.UtcNow;
        var source = string.IsNullOrWhiteSpace(req.Source) ? "manual" : req.Source!.Trim().ToLowerInvariant();
        var row = new CrabWeightHistory
        {
            CrabId = crab.Id,
            WeightGram = req.WeightGram,
            CarapaceWidthMm = req.CarapaceWidthMm,
            CarapaceLengthMm = req.CarapaceLengthMm,
            MeasuredAt = at,
            Source = source,
            Notes = req.Notes,
            RecordedByName = string.IsNullOrWhiteSpace(req.RecordedByName) ? null : req.RecordedByName.Trim(),
            PhotoUrlsJson = JsonStringList.Serialize(req.PhotoUrls)
        };
        await _uow.CrabWeightHistories.AddAsync(row, ct);

        crab.WeightGram = req.WeightGram;
        if (req.CarapaceWidthMm.HasValue) crab.CarapaceWidthMm = req.CarapaceWidthMm;
        if (req.CarapaceLengthMm.HasValue) crab.CarapaceLengthMm = req.CarapaceLengthMm;
        _uow.Crabs.Update(crab);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabWeightHistoryDto>.Ok(MapWeight(row), "Growth recorded.");
    }

    public async Task<ApiResponse<CrabWeightHistoryDto>> UpdateCrabWeightAsync(
        Guid crabId, Guid weightId, UpdateCrabWeightRequest req, CancellationToken ct = default)
    {
        var row = await _uow.CrabWeightHistories.GetByIdAsync(weightId, ct)
            ?? throw AppException.NotFound("CrabWeightHistory");
        if (row.CrabId != crabId) throw AppException.NotFound("CrabWeightHistory");
        if (req.Notes is not null) row.Notes = req.Notes;
        _uow.CrabWeightHistories.Update(row);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabWeightHistoryDto>.Ok(MapWeight(row), "Note updated.");
    }

    public async Task<ApiResponse<CrabGrowthMoltDto>> GetCrabGrowthMoltAsync(
        Guid crabId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        var weights = (await _uow.CrabWeightHistories.FindAsync(h => h.CrabId == crabId, ct))
            .Where(h => (!from.HasValue || h.MeasuredAt >= from.Value) && (!to.HasValue || h.MeasuredAt <= to.Value))
            .OrderBy(h => h.MeasuredAt)
            .Select(MapWeight)
            .ToList();
        var molts = (await _uow.MoltingRecords.FindAsync(m => m.CrabId == crabId, ct))
            .Where(m => (!from.HasValue || m.MoltTime >= from.Value) && (!to.HasValue || m.MoltTime <= to.Value))
            .OrderBy(m => m.MoltTime)
            .Select(FarmHistoryService.MapMolt)
            .ToList();
        return ApiResponse<CrabGrowthMoltDto>.Ok(new CrabGrowthMoltDto(
            weights, molts, crab.WeightGram, crab.CarapaceWidthMm, crab.CarapaceLengthMm));
    }

    public async Task<ApiResponse<CrabLifecycleEventsDto>> GetCrabLifecycleEventsAsync(
        Guid crabId,
        DateTime? from = null,
        DateTime? to = null,
        string? eventType = null,
        string? search = null,
        string? sort = null,
        int skip = 0,
        int take = 20,
        CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        var ctx = await LoadBoxContextAsync(ct);
        var lot = crab.CrabLotId != Guid.Empty
            ? await _uow.CrabLots.GetByIdAsync(crab.CrabLotId, ct)
            : null;

        var users = (await _uow.Users.GetAllAsync(ct)).ToDictionary(u => u.Id);
        var allocsAll = (await _uow.CrabBoxAllocations.FindAsync(a => a.CrabId == crabId, ct))
            .OrderBy(a => a.StartTime)
            .ToList();
        string ActorName(Guid? id)
        {
            if (id is Guid uid && users.TryGetValue(uid, out var u))
                return string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName;
            return "";
        }

        var events = new List<CrabLifecycleEventDto>();
        var crabCode = crab.Code ?? "";
        var crabKey = crab.Id.ToString();
        var currentBoxId = ResolveCurrentBoxId(crab);
        var currentBox = ctx.Boxes.FirstOrDefault(b => b.Id == currentBoxId);

        CrabLifecycleLocationDto? Loc(Guid? boxId)
        {
            var id = boxId is Guid g && g != Guid.Empty ? g : currentBoxId;
            if (id == Guid.Empty) return null;
            var box = ctx.Boxes.FirstOrDefault(b => b.Id == id);
            if (box is null) return null;
            ctx.Rows.TryGetValue(box.FarmingRowId, out var row);
            FarmingArea? area = null;
            if (row is not null) ctx.Areas.TryGetValue(row.FarmingAreaId, out area);
            return new CrabLifecycleLocationDto(
                area?.Id, area?.Code, row?.Id, row?.Code ?? row?.Name, box.Id, box.Code);
        }

        CrabLifecycleLocationDto? LocAt(DateTime at)
        {
            var hit = allocsAll
                .Where(a => a.StartTime <= at && (a.EndTime == null || a.EndTime > at))
                .OrderByDescending(a => a.StartTime)
                .FirstOrDefault();
            return Loc(hit?.BoxId ?? currentBoxId);
        }

        static CrabLifecycleActorDto Actor(string type, Guid? id, string name) =>
            new(type, id, string.IsNullOrWhiteSpace(name) ? type : name);

        static string MapSource(string? raw, bool hasCamera = false)
        {
            var s = (raw ?? "").Trim().ToLowerInvariant();
            if (s is "ai" or "auto" or "ai_camera") return "AI_CAMERA";
            if (s is "controller") return "CONTROLLER";
            if (s is "import") return "IMPORT";
            if (s is "system" or "assignment") return "SYSTEM";
            if (hasCamera && (s is "manual" or "manual_ai" or "")) return "MANUAL_AI";
            if (s is "transfer") return "MANUAL";
            return string.IsNullOrEmpty(s) ? "SYSTEM" : "MANUAL";
        }

        static Dictionary<string, CrabLifecycleChangeDto>? Changes(params (string Key, object? Before, object? After)[] items)
        {
            var map = items
                .Where(i => i.Before != null || i.After != null)
                .ToDictionary(i => i.Key, i => new CrabLifecycleChangeDto(i.Before, i.After));
            return map.Count == 0 ? null : map;
        }

        static Dictionary<string, object?> Meta(params (string Key, object? Value)[] items) =>
            items.Where(i => i.Value != null).ToDictionary(i => i.Key, i => i.Value);

        var importAt = lot?.ImportDate ?? crab.StockedAt;
        events.Add(new CrabLifecycleEventDto(
            $"SYS-{crab.Id:N}",
            crab.Id, crabCode, "SYSTEM", importAt,
            "Nhập hệ thống",
            string.IsNullOrWhiteSpace(lot?.LotCode) ? "Cua được đưa vào hệ thống" : $"Lô {lot!.LotCode}",
            Loc(currentBoxId),
            Actor("SYSTEM", null, "System"),
            "SYSTEM",
            null,
            null,
            Meta(("lotCode", lot?.LotCode)),
            Array.Empty<string>(),
            crab.Notes,
            null));

        var operations = (await _uow.FarmOperations.FindAsync(
                o => o.CrabIdsJson.Contains(crabKey), ct)).ToList();
        foreach (var op in operations)
        {
            var type = (op.Type ?? "").Trim().ToLowerInvariant();
            var photos = JsonStringList.Parse(op.PhotoUrlsJson);
            var loc = Loc(ParseFirstGuid(op.BoxIdsJson)) ?? LocAt(op.Timestamp);
            var actor = Actor(
                "USER",
                op.OperatorId == Guid.Empty ? null : op.OperatorId,
                string.IsNullOrWhiteSpace(op.OperatorName) ? "Người dùng" : op.OperatorName);
            var source = MapSource(op.Source, !string.IsNullOrWhiteSpace(op.CameraId));

            if (type == "feeding" || op.Quantity is not null || !string.IsNullOrWhiteSpace(op.Appetite))
            {
                var served = op.Quantity;
                var eaten = op.EatenQuantity;
                int? pct = served is > 0 && eaten is not null
                    ? (int)Math.Round((double)(eaten.Value / served.Value * 100m), MidpointRounding.AwayFromZero)
                    : null;
                var summaryParts = new List<string>();
                if (served is not null) summaryParts.Add($"Khẩu phần {FmtNum(served)} g");
                if (eaten is not null) summaryParts.Add($"Đã ăn {FmtNum(eaten)} g");
                if (pct is not null) summaryParts.Add($"{pct}%");
                var actSummary = ActivityChangeSummary(op.ActivityBefore, op.ActivityAfter);
                if (actSummary != null) summaryParts.Add(actSummary);
                events.Add(new CrabLifecycleEventDto(
                    $"FEED-{op.Id:N}",
                    crab.Id, crabCode, "FEEDING", op.Timestamp,
                    "Cho ăn",
                    summaryParts.Count == 0 ? "Phiếu cho ăn" : string.Join(" • ", summaryParts),
                    loc, actor, source, op.CameraId,
                    Changes(("activityScore", op.ActivityBefore, op.ActivityAfter)),
                    Meta(
                        ("servedGram", served),
                        ("eatenGram", eaten),
                        ("feedingPercent", pct),
                        ("foodType", op.FoodType),
                        ("appetite", op.Appetite),
                        ("appetiteLabel", AppetiteVi(op.Appetite)),
                        ("activityBefore", op.ActivityBefore),
                        ("activityAfter", op.ActivityAfter),
                        ("activityBeforeLabel", ActivityLabel(op.ActivityBefore)),
                        ("activityAfterLabel", ActivityLabel(op.ActivityAfter)),
                        ("feedingDurationMinutes", op.FeedingDurationMinutes),
                        ("feedingGrade", FeedingGrade(pct))),
                    photos, op.Notes, null));
            }
            else if (type == "note")
            {
                events.Add(new CrabLifecycleEventDto(
                    $"NOTE-{op.Id:N}",
                    crab.Id, crabCode, "NOTE_UPDATED", op.Timestamp,
                    "Ghi chú",
                    Truncate(op.Notes, 80) ?? "Cập nhật ghi chú",
                    loc, actor, source, op.CameraId, null,
                    Meta(("note", op.Notes)),
                    photos, op.Notes, null));
            }
        }

        var weights = (await _uow.CrabWeightHistories.FindAsync(h => h.CrabId == crabId, ct))
            .OrderBy(h => h.MeasuredAt)
            .ToList();
        CrabWeightHistory? prevW = null;
        foreach (var w in weights)
        {
            var dw = prevW is null ? (decimal?)null : w.WeightGram - prevW.WeightGram;
            var line = prevW is null
                ? $"Cân nặng {FmtNum(w.WeightGram)} g"
                : $"Cân nặng {FmtNum(prevW.WeightGram)} g → {FmtNum(w.WeightGram)} g";
            if (dw is not null) line += $" • {FmtSigned(dw)} g";
            var sizeAfter = SizePair(w.CarapaceWidthMm, w.CarapaceLengthMm);
            var sizeBefore = prevW is null ? null : SizePair(prevW.CarapaceWidthMm, prevW.CarapaceLengthMm);
            if (sizeAfter != null)
                line += sizeBefore == null ? $" • {sizeAfter}" : $" • {sizeBefore} → {sizeAfter}";
            events.Add(new CrabLifecycleEventDto(
                $"GRW-{w.Id:N}",
                crab.Id, crabCode, "GROWTH_UPDATE", w.MeasuredAt,
                "Cập nhật sinh trưởng",
                line,
                LocAt(w.MeasuredAt),
                Actor(
                    string.IsNullOrWhiteSpace(w.RecordedByName) ? "SYSTEM" : "USER",
                    null,
                    string.IsNullOrWhiteSpace(w.RecordedByName) ? "System" : w.RecordedByName),
                MapSource(w.Source),
                null,
                Changes(
                    ("weightGram", prevW?.WeightGram, w.WeightGram),
                    ("carapaceWidthMm", prevW?.CarapaceWidthMm, w.CarapaceWidthMm),
                    ("carapaceLengthMm", prevW?.CarapaceLengthMm, w.CarapaceLengthMm)),
                Meta(
                    ("weightBefore", prevW?.WeightGram),
                    ("weightAfter", w.WeightGram),
                    ("deltaGram", dw),
                    ("widthBefore", prevW?.CarapaceWidthMm),
                    ("widthAfter", w.CarapaceWidthMm),
                    ("lengthBefore", prevW?.CarapaceLengthMm),
                    ("lengthAfter", w.CarapaceLengthMm)),
                JsonStringList.Parse(w.PhotoUrlsJson),
                w.Notes,
                null));
            prevW = w;
        }

        var molts = (await _uow.MoltingRecords.FindAsync(m => m.CrabId == crabId, ct))
            .OrderBy(m => m.MoltTime)
            .ToList();
        var moltNo = 0;
        foreach (var m in molts)
        {
            moltNo++;
            var resultLabel = MoltResultVi(m.Result);
            var completedAt = m.CompletedAt ?? m.MoltTime;
            var startedAt = m.StartedAt;
            var duration = startedAt is DateTime st ? completedAt - st : (TimeSpan?)null;
            if (startedAt is DateTime start && start < completedAt.AddMinutes(-1))
            {
                events.Add(new CrabLifecycleEventDto(
                    $"MLS-{m.Id:N}",
                    crab.Id, crabCode, "MOLT_START", start,
                    "Bắt đầu lột xác",
                    $"Lần {moltNo}",
                    Loc(m.BoxId) ?? LocAt(start),
                    Actor(m.Source == "ai" ? "AI" : "USER", null, m.Source == "ai" ? "AI System" : "Người dùng"),
                    MapSource(m.Source, !string.IsNullOrWhiteSpace(m.CameraId)),
                    m.CameraId, null,
                    Meta(("moltNumber", moltNo), ("result", m.Result), ("resultLabel", resultLabel)),
                    JsonStringList.Parse(m.PhotoUrlsJson), m.Notes, null));
            }
            var parts = new List<string> { $"Lần {moltNo}", resultLabel };
            if (duration is TimeSpan d && d.TotalMinutes > 0) parts.Add(FmtDuration(d));
            if (m.WeightBeforeGram is not null && m.WeightAfterGram is not null)
                parts.Add($"{FmtNum(m.WeightBeforeGram)} g → {FmtNum(m.WeightAfterGram)} g");
            events.Add(new CrabLifecycleEventDto(
                $"MLC-{m.Id:N}",
                crab.Id, crabCode, "MOLT_COMPLETE", completedAt,
                "Hoàn tất lột xác",
                string.Join(" • ", parts),
                Loc(m.BoxId) ?? LocAt(completedAt),
                Actor(m.Source == "ai" ? "AI" : "USER", null, m.Source == "ai" ? "AI System" : "Người dùng"),
                MapSource(m.Source, !string.IsNullOrWhiteSpace(m.CameraId)),
                m.CameraId,
                Changes(
                    ("weightGram", m.WeightBeforeGram, m.WeightAfterGram),
                    ("carapaceWidthMm", m.ShellWidthBeforeMm, m.ShellWidthAfterMm),
                    ("carapaceLengthMm", m.ShellLengthBeforeMm, m.ShellLengthAfterMm)),
                Meta(
                    ("moltNumber", moltNo),
                    ("result", m.Result),
                    ("resultLabel", resultLabel),
                    ("durationMinutes", duration is TimeSpan td ? (int)td.TotalMinutes : null),
                    ("weightBefore", m.WeightBeforeGram),
                    ("weightAfter", m.WeightAfterGram),
                    ("widthBefore", m.ShellWidthBeforeMm),
                    ("widthAfter", m.ShellWidthAfterMm),
                    ("lengthBefore", m.ShellLengthBeforeMm),
                    ("lengthAfter", m.ShellLengthAfterMm)),
                JsonStringList.Parse(m.PhotoUrlsJson),
                m.Notes,
                string.Equals(m.Result, "failed", StringComparison.OrdinalIgnoreCase) ? "critical"
                    : string.Equals(m.Result, "abnormal", StringComparison.OrdinalIgnoreCase) ? "warning"
                    : null));
        }

        var statuses = (await _uow.CrabStatusHistories.FindAsync(h => h.CrabId == crabId, ct)).ToList();
        foreach (var h in statuses)
        {
            var src = (h.Source ?? "").Trim().ToLowerInvariant();
            if (src is "transfer" or "assignment") continue;

            var actorName = ActorName(h.ChangedByUserId);
            var actor = string.IsNullOrWhiteSpace(actorName)
                ? Actor(src is "ai" or "auto" ? "AI" : "SYSTEM", h.ChangedByUserId, src is "ai" or "auto" ? "AI System" : "System")
                : Actor("USER", h.ChangedByUserId, actorName);
            var beforeLabel = ConditionVi(h.OldCondition, h.OldStatus);
            var afterLabel = ConditionVi(h.NewCondition, h.NewStatus);
            var eventTypeName = h.NewStatus switch
            {
                CrabStatus.Harvested => "HARVESTED",
                CrabStatus.Sold => "HARVESTED",
                CrabStatus.Dead => "DEAD",
                _ when (h.Reason ?? "").StartsWith("CRAB_CREATED", StringComparison.OrdinalIgnoreCase)
                    => "CRAB_CREATED",
                _ when (h.Reason ?? "").StartsWith("CRAB_PROFILE_UPDATED", StringComparison.OrdinalIgnoreCase)
                    => "CRAB_PROFILE_UPDATED",
                _ when (h.Reason ?? "").Contains("sắp thu hoạch", StringComparison.OrdinalIgnoreCase)
                    || (h.Reason ?? "").Contains("ready", StringComparison.OrdinalIgnoreCase)
                    => "HARVEST_READY",
                _ => "HEALTH_CHECK"
            };
            var title = eventTypeName switch
            {
                "HARVESTED" => h.NewStatus == CrabStatus.Sold ? "Đã bán" : "Thu hoạch",
                "DEAD" => "Ghi nhận chết",
                "HARVEST_READY" => "Sẵn sàng thu hoạch",
                "CRAB_CREATED" => "Tạo cá thể",
                "CRAB_PROFILE_UPDATED" => "Cập nhật thông tin cua",
                _ => "Kiểm tra sức khỏe"
            };
            var line = eventTypeName == "CRAB_CREATED"
                ? string.Join(" · ", new[]
                {
                    string.IsNullOrWhiteSpace(lot?.LotCode) ? null : $"Lô {lot!.LotCode}",
                    crab.WeightGram is decimal wg ? $"{FmtNum(wg)} g" : null,
                    SizePair(crab.CarapaceWidthMm, crab.CarapaceLengthMm)
                }.Where(s => !string.IsNullOrWhiteSpace(s)))
                : beforeLabel != null && afterLabel != null && beforeLabel != afterLabel
                    ? $"{beforeLabel} → {afterLabel}"
                    : afterLabel ?? h.Reason ?? title;
            events.Add(new CrabLifecycleEventDto(
                $"HLT-{h.Id:N}",
                crab.Id, crabCode, eventTypeName, h.ChangedAt,
                title, line,
                LocAt(h.ChangedAt), actor, MapSource(h.Source), null,
                Changes(("condition", beforeLabel, afterLabel), ("status", h.OldStatus?.ToString(), h.NewStatus.ToString())),
                Meta(
                    ("conditionBefore", beforeLabel),
                    ("conditionAfter", afterLabel),
                    ("statusBefore", h.OldStatus?.ToString()),
                    ("statusAfter", h.NewStatus.ToString())),
                Array.Empty<string>(),
                h.Reason,
                h.NewCondition is CrabCondition.Problem or CrabCondition.Dead ? "warning" : null));
        }

        CrabBoxAllocation? prevAlloc = null;
        foreach (var a in allocsAll)
        {
            if (prevAlloc is null)
            {
                prevAlloc = a;
                continue;
            }
            var fromBox = ctx.Boxes.FirstOrDefault(b => b.Id == prevAlloc.BoxId);
            var toBox = ctx.Boxes.FirstOrDefault(b => b.Id == a.BoxId);
            events.Add(new CrabLifecycleEventDto(
                $"TRF-{a.Id:N}",
                crab.Id, crabCode, "BOX_TRANSFER", a.StartTime,
                "Chuyển hộp",
                $"{fromBox?.Code ?? "—"} → {toBox?.Code ?? "—"}",
                Loc(a.BoxId),
                Actor("USER", null, "Người dùng"),
                "MANUAL",
                null,
                Changes(("boxId", fromBox?.Code ?? prevAlloc.BoxId.ToString(), toBox?.Code ?? a.BoxId.ToString())),
                Meta(
                    ("fromBoxId", prevAlloc.BoxId.ToString()),
                    ("fromBoxCode", fromBox?.Code),
                    ("toBoxId", a.BoxId.ToString()),
                    ("toBoxCode", toBox?.Code),
                    ("reason", a.Notes)),
                Array.Empty<string>(),
                a.Notes,
                null));
            prevAlloc = a;
        }

        var analyses = (await _uow.CrabAiAnalyses.FindAsync(a => a.CrabId == crabId, ct)).ToList();
        foreach (var a in analyses)
        {
            var anomaly = !string.IsNullOrWhiteSpace(a.AnomalyNote);
            var low = (a.ActivityLevel ?? "").Contains("low", StringComparison.OrdinalIgnoreCase)
                || (a.ActivityLevel ?? "").Contains("thấp", StringComparison.OrdinalIgnoreCase);
            var title = anomaly || low ? "AI phát hiện" : "AI kiểm tra";
            var line = anomaly
                ? a.AnomalyNote!
                : low
                    ? a.ActivityLevel ?? "Giảm vận động bất thường"
                    : string.IsNullOrWhiteSpace(a.Prediction)
                        ? "Không phát hiện bất thường"
                        : a.Prediction;
            var media = string.IsNullOrWhiteSpace(a.MediaUrl)
                ? Array.Empty<string>()
                : new[] { a.MediaUrl! };
            events.Add(new CrabLifecycleEventDto(
                $"AI-{a.Id:N}",
                crab.Id, crabCode, "AI_DETECTION", a.AnalyzedAt,
                title, line,
                Loc(a.BoxId) ?? LocAt(a.AnalyzedAt),
                Actor("AI", null, "AI System"),
                "AI_CAMERA",
                currentBox is null ? null : null,
                Changes(("activityLevel", null, a.ActivityLevel), ("prediction", null, a.Prediction)),
                Meta(
                    ("prediction", a.Prediction),
                    ("confidence", a.Confidence),
                    ("activityLevel", a.ActivityLevel),
                    ("anomalyNote", a.AnomalyNote),
                    ("modelVersion", a.ModelVersion)),
                media,
                a.AnomalyNote,
                anomaly || low ? "warning" : null));
        }

        var harvests = (await _uow.CrabHarvestHistories.FindAsync(h => h.CrabId == crabId, ct)).ToList();
        foreach (var h in harvests)
        {
            events.Add(new CrabLifecycleEventDto(
                $"HRV-{h.Id:N}",
                crab.Id, crabCode, "HARVESTED", h.HarvestedAt,
                "Thu hoạch",
                string.Join(" • ", new[] { h.Grade, h.WeightGram is null ? null : $"{FmtNum(h.WeightGram)} g" }.Where(x => !string.IsNullOrWhiteSpace(x))),
                LocAt(h.HarvestedAt),
                Actor("USER", null, "Người dùng"),
                "MANUAL",
                null,
                Changes(("status", "Alive", "Harvested")),
                Meta(("grade", h.Grade), ("weightGram", h.WeightGram)),
                Array.Empty<string>(),
                h.Notes,
                null));
        }

        var deaths = (await _uow.CrabMortalityRecords.FindAsync(h => h.CrabId == crabId, ct)).ToList();
        foreach (var d in deaths)
        {
            var name = ActorName(d.RecordedBy);
            events.Add(new CrabLifecycleEventDto(
                $"DED-{d.Id:N}",
                crab.Id, crabCode, "DEAD", d.MortalityDate,
                "Ghi nhận chết",
                d.Notes ?? d.Cause.ToString(),
                LocAt(d.MortalityDate),
                string.IsNullOrWhiteSpace(name) ? Actor("SYSTEM", d.RecordedBy, "System") : Actor("USER", d.RecordedBy, name),
                "MANUAL",
                null,
                Changes(("status", "Alive", "Dead")),
                Meta(("cause", d.Cause.ToString())),
                Array.Empty<string>(),
                d.Notes,
                "critical"));
        }

        var code = crab.Code ?? "";
        var boxCode = currentBox?.Code ?? "";
        var alerts = (await _uow.Alerts.GetAllAsync(ct))
            .Where(a =>
                (!string.IsNullOrWhiteSpace(code) && a.Message.Contains(code, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(boxCode) && a.Message.Contains(boxCode, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        foreach (var a in alerts)
        {
            var sev = a.Severity.ToString().ToLowerInvariant();
            events.Add(new CrabLifecycleEventDto(
                $"ALR-{a.Id:N}",
                crab.Id, crabCode, "ALERT_CREATED", a.CreatedAt,
                "Cảnh báo",
                a.Message,
                Loc(currentBoxId),
                Actor("SYSTEM", null, "System"),
                "SYSTEM",
                null, null,
                Meta(("severity", sev), ("severityLabel", AlertSevVi(a.Severity)), ("status", a.Status.ToString())),
                Array.Empty<string>(),
                a.Message,
                sev));
            if (a.Status == AlertStatus.Resolved || a.AcknowledgedAt is not null)
            {
                events.Add(new CrabLifecycleEventDto(
                    $"ALX-{a.Id:N}",
                    crab.Id, crabCode, "ALERT_RESOLVED", a.AcknowledgedAt ?? a.UpdatedAt ?? a.CreatedAt,
                    "Xác nhận cảnh báo",
                    a.Message,
                    Loc(currentBoxId),
                    Actor("USER", a.AcknowledgedBy, string.IsNullOrWhiteSpace(ActorName(a.AcknowledgedBy)) ? "Người dùng" : ActorName(a.AcknowledgedBy)),
                    "MANUAL",
                    null, null,
                    Meta(("severity", sev), ("status", a.Status.ToString())),
                    Array.Empty<string>(),
                    a.Message,
                    null));
            }
        }

        IEnumerable<CrabLifecycleEventDto> filtered = events;
        if (from.HasValue)
            filtered = filtered.Where(e => e.OccurredAt >= from.Value);
        if (to.HasValue)
            filtered = filtered.Where(e => e.OccurredAt <= to.Value);

        var q = (search ?? "").Trim();
        if (q.Length > 0)
        {
            filtered = filtered.Where(e => EventMatchesSearch(e, q));
        }

        var dated = filtered.ToList();
        var summary = new CrabLifecycleSummaryDto(
            dated.Count,
            dated.Count(e => e.EventType == "FEEDING"),
            dated.Count(e => e.EventType == "GROWTH_UPDATE"),
            dated.Count(e => e.EventType is "MOLT_START" or "MOLT_COMPLETE"),
            dated.Count(e => e.EventType == "HEALTH_CHECK"),
            dated.Count(e => e.EventType == "BOX_TRANSFER"),
            dated.Count(e => e.EventType == "AI_DETECTION"),
            dated.Count(e => e.EventType is "ALERT_CREATED" or "ALERT_RESOLVED"),
            dated.Count(e => e.EventType is "HARVESTED" or "HARVEST_READY"),
            dated.Count(e => e.EventType == "SYSTEM"),
            dated.Count(e => e.EventType == "NOTE_UPDATED"));

        var typeKey = (eventType ?? "").Trim().ToUpperInvariant();
        if (typeKey is not ("" or "ALL" or "TẤT CẢ"))
        {
            dated = dated.Where(e => EventTypeMatches(e.EventType, typeKey)).ToList();
        }

        var desc = !string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase);
        dated = desc
            ? dated.OrderByDescending(e => e.OccurredAt).ToList()
            : dated.OrderBy(e => e.OccurredAt).ToList();

        if (skip < 0) skip = 0;
        if (take <= 0) take = 20;
        if (take > 500) take = 500;
        var page = dated.Skip(skip).Take(take).ToList();
        return ApiResponse<CrabLifecycleEventsDto>.Ok(new CrabLifecycleEventsDto(
            page, dated.Count, skip + page.Count < dated.Count, summary));
    }

    private static bool EventTypeMatches(string eventType, string filter) => filter switch
    {
        "FEEDING" or "CHOAN" or "CHO_AN" => eventType == "FEEDING",
        "GROWTH" or "GROWTH_UPDATE" or "SINHTRUONG" => eventType == "GROWTH_UPDATE",
        "MOLT" or "MOLT_START" or "MOLT_COMPLETE" or "LOTXAC" => eventType is "MOLT_START" or "MOLT_COMPLETE",
        "HEALTH" or "HEALTH_CHECK" or "SUCKHOE" => eventType == "HEALTH_CHECK",
        "TRANSFER" or "BOX_TRANSFER" or "CHUYENHOP" => eventType == "BOX_TRANSFER",
        "AI" or "AI_DETECTION" => eventType == "AI_DETECTION",
        "ALERT" or "ALERT_CREATED" or "ALERT_RESOLVED" or "CANHBAO" => eventType is "ALERT_CREATED" or "ALERT_RESOLVED",
        "HARVEST" or "HARVESTED" or "HARVEST_READY" or "THUHOACH" => eventType is "HARVESTED" or "HARVEST_READY",
        "SYSTEM" or "HETHONG" => eventType == "SYSTEM",
        "NOTE" or "NOTE_UPDATED" or "GHICHU" => eventType == "NOTE_UPDATED",
        "PROFILE" or "CRAB_PROFILE_UPDATED" => eventType == "CRAB_PROFILE_UPDATED",
        "CREATED" or "CRAB_CREATED" or "TAOCATHE" => eventType == "CRAB_CREATED",
        "DEAD" => eventType == "DEAD",
        _ => eventType.Equals(filter, StringComparison.OrdinalIgnoreCase)
    };

    private static bool EventMatchesSearch(CrabLifecycleEventDto e, string q)
    {
        var hay = string.Join(' ', new[]
        {
            e.Title, e.Summary, e.Note, e.EventType, e.Source, e.CameraId,
            e.Actor.Name, e.Location?.BoxCode, e.Location?.RowCode, e.Location?.FarmAreaCode,
            e.CrabCode
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return hay.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static Guid? ParseFirstGuid(string? json)
    {
        foreach (var s in JsonStringList.Parse(json))
            if (Guid.TryParse(s, out var g) && g != Guid.Empty) return g;
        return null;
    }

    private static string FmtNum(decimal? v)
    {
        if (v is null) return "—";
        return v.Value == decimal.Truncate(v.Value) ? v.Value.ToString("0") : v.Value.ToString("0.#");
    }

    private static string FmtSigned(decimal? v)
    {
        if (v is null) return "—";
        var n = FmtNum(v);
        return v >= 0 ? $"+{n}" : n;
    }

    private static string? SizePair(decimal? w, decimal? l)
    {
        if (w is null && l is null) return null;
        return $"{FmtNum(w)} × {FmtNum(l)} mm";
    }

    private static string FmtDuration(TimeSpan d)
    {
        if (d.TotalHours >= 1)
            return d.Minutes == 0 ? $"{(int)d.TotalHours} giờ" : $"{(int)d.TotalHours} giờ {d.Minutes} phút";
        return $"{(int)d.TotalMinutes} phút";
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static string? ActivityLabel(int? score)
    {
        if (score is null) return null;
        if (score <= 30) return "Thấp";
        if (score <= 70) return "Bình thường";
        return "Cao";
    }

    private static string? ActivityChangeSummary(int? before, int? after)
    {
        var a = ActivityLabel(before);
        var b = ActivityLabel(after);
        if (a == null && b == null) return null;
        if (a != null && b != null && a != b) return $"Vận động {a} → {b}";
        return $"Vận động {b ?? a}";
    }

    private static string? FeedingGrade(int? pct) => pct switch
    {
        null => null,
        >= 80 => "Tốt",
        >= 50 => "Theo dõi",
        _ => "Cảnh báo"
    };

    private static string MoltResultVi(string? result) => (result ?? "").Trim().ToLowerInvariant() switch
    {
        "success" or "normal" => "Bình thường",
        "monitoring" => "Theo dõi",
        "abnormal" => "Bất thường",
        "failed" => "Lột thất bại",
        _ => string.IsNullOrWhiteSpace(result) ? "Bình thường" : result
    };

    private static string? ConditionVi(CrabCondition? condition, CrabStatus? status)
    {
        if (status == CrabStatus.Harvested) return "Thu hoạch";
        if (status == CrabStatus.Sold) return "Đã bán";
        if (status == CrabStatus.Dead) return "Đã chết";
        return condition switch
        {
            null => null,
            CrabCondition.Normal => "Khỏe mạnh",
            CrabCondition.Premolt => "Sắp lột",
            CrabCondition.Molting => "Đang lột",
            CrabCondition.Softshell => "Cua mềm",
            CrabCondition.Problem => "Có vấn đề",
            CrabCondition.Weak => "Theo dõi",
            CrabCondition.Dead => "Đã chết",
            CrabCondition.Harvested => "Thu hoạch",
            CrabCondition.Sold => "Đã bán",
            _ => condition.ToString()
        };
    }

    private static string AlertSevVi(AlertSeverity s) => s switch
    {
        AlertSeverity.Critical => "Nghiêm trọng",
        AlertSeverity.Warning => "Theo dõi",
        _ => "Thông tin"
    };

    private static CrabWeightHistoryDto MapWeight(CrabWeightHistory h) =>
        new(h.Id, h.CrabId, h.WeightGram, h.MeasuredAt, h.Source, h.Notes,
            h.CarapaceWidthMm, h.CarapaceLengthMm, h.RecordedByName,
            JsonStringList.Parse(h.PhotoUrlsJson));

    public async Task<ApiResponse<IReadOnlyList<CrabAiAnalysisDto>>> GetCrabAiAnalysesAsync(
        Guid crabId, CancellationToken ct = default)
    {
        _ = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        var rows = (await _uow.CrabAiAnalyses.FindAsync(h => h.CrabId == crabId, ct))
            .OrderByDescending(h => h.AnalyzedAt)
            .Select(h => new CrabAiAnalysisDto(
                h.Id, h.CrabId, h.BoxId, h.Prediction, h.Confidence,
                h.ActivityLevel, h.AnomalyNote, h.MediaUrl, h.ModelVersion, h.AnalyzedAt))
            .ToList();
        return ApiResponse<IReadOnlyList<CrabAiAnalysisDto>>.Ok(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<CrabHarvestHistoryDto>>> GetCrabHarvestHistoryAsync(
        Guid crabId, CancellationToken ct = default)
    {
        _ = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
        var rows = (await _uow.CrabHarvestHistories.FindAsync(h => h.CrabId == crabId, ct))
            .OrderByDescending(h => h.HarvestedAt)
            .Select(h => new CrabHarvestHistoryDto(
                h.Id, h.CrabId, h.HarvestLineId, h.HarvestedAt, h.WeightGram, h.Grade, h.Notes))
            .ToList();
        return ApiResponse<IReadOnlyList<CrabHarvestHistoryDto>>.Ok(rows);
    }

    // ─── Hierarchy helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Dọn mọi bản ghi con trỏ tới cua/hộp sắp bị xoá, trước khi xoá cha.
    /// Gồm 2 loại:
    ///  - FK "NO ACTION": không dọn trước thì DB chặn, cả transaction fail.
    ///  - Cột tham chiếu KHÔNG có FK: không dọn thì thành bản ghi mồ côi.
    /// Các FK đã là CASCADE (CrabBoxAllocations/BoxStatusHistories/MoltingRecords
    /// theo CrabId, CrabMortalityRecords) vẫn liệt kê tường minh cho chắc.
    /// ponytail: quét theo danh sách Id trong bộ nhớ — thao tác admin hiếm, phạm vi 1 khu.
    /// </summary>
    private async Task RemoveDependentsAsync(
        IReadOnlyCollection<Guid> crabIds, IReadOnlyCollection<Guid> boxIds, CancellationToken ct)
    {
        var qrCodes = new List<QrCode>();

        if (crabIds.Count > 0)
        {
            await RemoveWhereAsync(_uow.AiDetections, x => x.CrabId != null && crabIds.Contains(x.CrabId.Value), ct);
            await RemoveWhereAsync(_uow.HarvestLines, x => x.CrabId != null && crabIds.Contains(x.CrabId.Value), ct);
            await RemoveWhereAsync(_uow.Inspections, x => x.CrabId != null && crabIds.Contains(x.CrabId.Value), ct);
            await RemoveWhereAsync(_uow.MediaAssets, x => x.CrabId != null && crabIds.Contains(x.CrabId.Value), ct);
            await RemoveWhereAsync(_uow.SalesOrderLines, x => x.CrabId != null && crabIds.Contains(x.CrabId.Value), ct);

            await RemoveWhereAsync(_uow.MoltingRecords, x => crabIds.Contains(x.CrabId), ct);
            await RemoveWhereAsync(_uow.CrabBoxAllocations, x => crabIds.Contains(x.CrabId), ct);
            await RemoveWhereAsync(_uow.CrabMortalityRecords, x => crabIds.Contains(x.CrabId), ct);
            await RemoveWhereAsync(_uow.CrabStatusHistories, x => crabIds.Contains(x.CrabId), ct);
            await RemoveWhereAsync(_uow.CrabWeightHistories, x => crabIds.Contains(x.CrabId), ct);
            await RemoveWhereAsync(_uow.CrabHarvestHistories, x => crabIds.Contains(x.CrabId), ct);
            await RemoveWhereAsync(_uow.CrabAiAnalyses, x => crabIds.Contains(x.CrabId), ct);

            qrCodes.AddRange(await _uow.QrCodes.FindAsync(
                q => q.CrabId != null && crabIds.Contains(q.CrabId.Value), ct));
        }

        if (boxIds.Count > 0)
        {
            await RemoveWhereAsync(_uow.HarvestLines, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);
            await RemoveWhereAsync(_uow.MediaAssets, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);
            await RemoveWhereAsync(_uow.MoltingRecords, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);

            await RemoveWhereAsync(_uow.BoxStatusHistories, x => boxIds.Contains(x.BoxId), ct);
            await RemoveWhereAsync(_uow.CrabBoxAllocations, x => boxIds.Contains(x.BoxId), ct);
            await RemoveWhereAsync(_uow.AiDetections, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);
            await RemoveWhereAsync(_uow.CrabAiAnalyses, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);
            await RemoveWhereAsync(_uow.Inspections, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);
            await RemoveWhereAsync(_uow.SaleTransactions, x => x.BoxId != null && boxIds.Contains(x.BoxId.Value), ct);

            qrCodes.AddRange(await _uow.QrCodes.FindAsync(
                q => q.BoxId != null && boxIds.Contains(q.BoxId.Value), ct));
        }

        // QR là cha của TraceabilityLink -> xoá liên kết trước rồi mới xoá QR.
        var qrIds = qrCodes.Select(q => q.Id).Distinct().ToList();
        if (qrIds.Count > 0)
            await RemoveWhereAsync(_uow.TraceabilityLinks, x => qrIds.Contains(x.QrCodeId), ct);

        foreach (var qr in qrCodes.DistinctBy(q => q.Id))
            _uow.QrCodes.Remove(qr);
    }

    /// <summary>
    /// FarmOperations.BoxIdsJson / CrabIdsJson là tham chiếu mềm (không FK).
    /// Không dọn thì JSON còn giữ Id của hộp / cua vừa bị xoá.
    /// </summary>
    private async Task ClearOperationRefsAsync(
        IReadOnlyCollection<Guid> boxIds, IReadOnlyCollection<Guid> crabIds, CancellationToken ct)
    {
        if (boxIds.Count == 0 && crabIds.Count == 0)
            return;

        foreach (var op in await _uow.FarmOperations.GetAllAsync(ct))
        {
            var changed = false;

            if (boxIds.Count > 0 && TryRemoveRefs(op.BoxIdsJson, boxIds, out var boxesJson))
            {
                op.BoxIdsJson = boxesJson;
                changed = true;
            }
            if (crabIds.Count > 0 && TryRemoveRefs(op.CrabIdsJson, crabIds, out var crabsJson))
            {
                op.CrabIdsJson = crabsJson;
                changed = true;
            }

            if (changed)
                _uow.FarmOperations.Update(op);
        }
    }

    /// <summary>Bỏ các Id đã xoá khỏi mảng JSON. Trả về true nếu có thay đổi.</summary>
    private static bool TryRemoveRefs(
        string? json, IReadOnlyCollection<Guid> goneIds, out string updated)
    {
        updated = json ?? "[]";
        if (string.IsNullOrWhiteSpace(json))
            return false;

        List<string>? ids;
        try
        {
            ids = JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return false; // JSON hỏng: không đoán, để nguyên
        }

        if (ids == null)
            return false;

        var gone = goneIds.Select(g => g.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = ids.Where(x => !gone.Contains(x)).ToList();
        if (kept.Count == ids.Count)
            return false;

        updated = JsonSerializer.Serialize(kept);
        return true;
    }

    private async Task RemoveWhereAsync<T>(
        IRepository<T> repo, Expression<Func<T, bool>> predicate, CancellationToken ct)
        where T : class
    {
        foreach (var entity in await repo.FindAsync(predicate, ct))
            repo.Remove(entity);
    }

    private async Task<FarmingArea> RequireAreaAsync(Guid id, bool requireActive, CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw AppException.BadRequest("FarmingAreaId is required.");
        var area = await _uow.FarmingAreas.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("FarmingArea");
        if (requireActive && !area.IsActive)
            throw AppException.BadRequest($"Farming area '{area.Name}' is inactive.");
        return area;
    }

    private async Task<FarmingRow> RequireRowAsync(Guid id, bool requireActive, CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw AppException.BadRequest("FarmingRowId is required.");
        var row = await _uow.FarmingRows.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("FarmingRow");
        if (requireActive && !row.IsActive)
            throw AppException.BadRequest($"Farming row '{row.Name}' is inactive.");
        return row;
    }

    private async Task<Box> RequireBoxAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty)
            throw AppException.BadRequest("BoxId is required.");
        return await _uow.Boxes.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Box");
    }

    /// <summary>Boxes with no live crab (prefer IsOccupied=false / status empty).</summary>
    private async Task<List<Box>> ListEmptyBoxesAsync(
        Guid? farmingAreaId, Guid? farmingRowId, CancellationToken ct)
    {
        var ctx = await LoadBoxContextAsync(ct);
        var aliveCrabs = await _uow.Crabs.Query()
    .Include(c => c.BoxAllocations)
    .Where(c => c.Status == CrabStatus.Alive
             || c.Status == CrabStatus.Molting
             || c.Status == CrabStatus.Quarantined)
    .ToListAsync(ct);

        var liveBoxIds = aliveCrabs
            .Select(c => CurrentBoxId(c))
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        IEnumerable<Box> q = ctx.Boxes.Where(b => !liveBoxIds.Contains(b.Id));
        if (farmingRowId.HasValue && farmingRowId != Guid.Empty)
            q = q.Where(b => b.FarmingRowId == farmingRowId.Value);
        else if (farmingAreaId.HasValue && farmingAreaId != Guid.Empty)
        {
            var rowIds = ctx.Rows.Values
                .Where(r => r.FarmingAreaId == farmingAreaId.Value && r.IsActive)
                .Select(r => r.Id)
                .ToHashSet();
            q = q.Where(b => rowIds.Contains(b.FarmingRowId));
        }

        return q.OrderBy(b => b.Code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<Box?> PickEmptyBoxAsync(Guid farmingAreaId, Guid? farmingRowId, CancellationToken ct)
    {
        var empty = await ListEmptyBoxesAsync(farmingAreaId, farmingRowId, ct);
        return empty.FirstOrDefault();
    }

    private async Task<FarmAvailabilityDto> BuildAvailabilityAsync(
        Guid? farmingAreaId, Guid? farmingRowId, CancellationToken ct)
    {
        var ctx = await LoadBoxContextAsync(ct);
        var emptyBoxes = await ListEmptyBoxesAsync(farmingAreaId, farmingRowId, ct);
        var emptyIds = emptyBoxes.Select(b => b.Id).ToHashSet();

        IEnumerable<Box> scopedBoxes = ctx.Boxes;
        if (farmingRowId.HasValue && farmingRowId != Guid.Empty)
            scopedBoxes = scopedBoxes.Where(b => b.FarmingRowId == farmingRowId.Value);
        else if (farmingAreaId.HasValue && farmingAreaId != Guid.Empty)
        {
            var rowIds = ctx.Rows.Values
                .Where(r => r.FarmingAreaId == farmingAreaId.Value)
                .Select(r => r.Id).ToHashSet();
            scopedBoxes = scopedBoxes.Where(b => rowIds.Contains(b.FarmingRowId));
        }

        var scopedList = scopedBoxes.ToList();
        var occupiedCount = scopedList.Count(b => !emptyIds.Contains(b.Id));

        var emptyDtos = emptyBoxes.Select(b =>
        {
            ctx.Rows.TryGetValue(b.FarmingRowId, out var row);
            FarmingArea? area = null;
            if (row is not null) ctx.Areas.TryGetValue(row.FarmingAreaId, out area);
            return new AvailableBoxDto(
                b.Id, b.Code, b.FarmingRowId, row?.FarmingAreaId ?? Guid.Empty,
                row?.Name, area?.Name, b.Status);
        }).ToList();

        IEnumerable<FarmingRow> rows = ctx.Rows.Values.Where(r => r.IsActive);
        if (farmingRowId.HasValue && farmingRowId != Guid.Empty)
            rows = rows.Where(r => r.Id == farmingRowId.Value);
        else if (farmingAreaId.HasValue && farmingAreaId != Guid.Empty)
            rows = rows.Where(r => r.FarmingAreaId == farmingAreaId.Value);

        var rowDtos = rows.OrderBy(r => r.Name).Select(r =>
        {
            ctx.Areas.TryGetValue(r.FarmingAreaId, out var area);
            var boxesInRow = ctx.Boxes.Where(b => b.FarmingRowId == r.Id).ToList();
            var emptyInRow = boxesInRow.Count(b => emptyIds.Contains(b.Id));
            var freeSlots = r.Capacity <= 0 ? -1 : Math.Max(0, r.Capacity - boxesInRow.Count);
            return new RowAvailabilityDto(
                r.Id, r.FarmingAreaId, r.Name, area?.Name,
                r.Capacity, boxesInRow.Count, emptyInRow, freeSlots);
        }).ToList();

        return new FarmAvailabilityDto(
            emptyDtos.Count,
            occupiedCount,
            emptyDtos.FirstOrDefault(),
            emptyDtos,
            rowDtos);
    }

    private sealed class BoxContext
    {
        public List<Box> Boxes { get; init; } = [];
        public Dictionary<Guid, FarmingRow> Rows { get; init; } = new();
        public Dictionary<Guid, FarmingArea> Areas { get; init; } = new();
        public Dictionary<Guid, List<Crab>> CrabsByBox { get; init; } = new();
        public Dictionary<Guid, Guid> BoxByCrab { get; init; } = new();
        /// <summary>CrabId → StartTime của allocation đang mở.</summary>
        public Dictionary<Guid, DateTime> InBoxSinceByCrab { get; init; } = new();
        public List<Alert> ActiveAlerts { get; init; } = [];
    }

    private async Task<BoxContext> LoadBoxContextAsync(CancellationToken ct)
    {
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);
        var (crabsByBox, boxByCrab, inBoxSince) = await LoadLiveCrabLocationsAsync(ct);
        var alerts = (await _uow.Alerts.GetAllAsync(ct))
            .Where(a => a.Status == AlertStatus.Active)
            .ToList();
        return new BoxContext
        {
            Boxes = boxes,
            Rows = rows,
            Areas = areas,
            CrabsByBox = crabsByBox,
            BoxByCrab = boxByCrab,
            InBoxSinceByCrab = inBoxSince,
            ActiveAlerts = alerts
        };
    }

    /// <summary>
    /// Cua đang nuôi → hộp: ưu tiên Crab.BoxId, rồi allocation đang mở (EndTime=null).
    /// Không dùng allocation đã đóng — tránh đếm nhầm cua đã thu hoạch.
    /// </summary>
    private async Task<(
            Dictionary<Guid, List<Crab>> ByBox,
            Dictionary<Guid, Guid> BoxByCrab,
            Dictionary<Guid, DateTime> InBoxSinceByCrab)>
        LoadLiveCrabLocationsAsync(CancellationToken ct)
    {
        var crabs = (await _uow.Crabs.GetAllAsync(ct) ?? Enumerable.Empty<Crab>())
            .Where(IsCrabAlive)
            .ToList();
        var crabIds = crabs.Select(c => c.Id).ToHashSet();
        var allocRepo = _uow.CrabBoxAllocations;
        var openAllocs = (crabIds.Count == 0 || allocRepo is null)
            ? new List<CrabBoxAllocation>()
            : (await allocRepo.FindAsync(
                a => a.EndTime == null && crabIds.Contains(a.CrabId), ct)
              ?? Enumerable.Empty<CrabBoxAllocation>()).ToList();
        var latestOpenByCrab = openAllocs
            .GroupBy(a => a.CrabId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.StartTime).First());
        var allocByCrab = latestOpenByCrab.ToDictionary(kv => kv.Key, kv => kv.Value.BoxId);
        var inBoxSinceByCrab = latestOpenByCrab.ToDictionary(kv => kv.Key, kv => kv.Value.StartTime);

        var boxByCrab = new Dictionary<Guid, Guid>();
        var byBox = new Dictionary<Guid, List<Crab>>();
        foreach (var crab in crabs)
        {
            var boxId = ResolveCurrentBoxId(crab, allocByCrab);
            if (boxId == Guid.Empty) continue;
            boxByCrab[crab.Id] = boxId;
            if (!byBox.TryGetValue(boxId, out var list))
            {
                list = [];
                byBox[boxId] = list;
            }
            list.Add(crab);
        }

        return (byBox, boxByCrab, inBoxSinceByCrab);
    }

    // ─── Paging / mapping ───────────────────────────────────────────────────

    private static PagedResult<TDto> Page<T, TDto>(
        IEnumerable<T> source, int page, int? pageSize, Func<T, TDto> map)
    {
        var list = source.ToList();
        if (pageSize is null or < 1)
        {
            return new PagedResult<TDto>
            {
                Items = list.Select(map),
                TotalCount = list.Count,
                Page = 1,
                PageSize = list.Count
            };
        }

        page = page < 1 ? 1 : page;
        var size = pageSize.Value > 500 ? 500 : pageSize.Value;
        return new PagedResult<TDto>
        {
            Items = list.Skip((page - 1) * size).Take(size).Select(map),
            TotalCount = list.Count,
            Page = page,
            PageSize = size
        };
    }

    private sealed record AreaStats(
        int RowCount,
        int BoxCount,
        int CrabCount,
        int HealthyBoxCount,
        int AlertBoxCount,
        int OccupiedBoxCount = 0,
        int WatchBoxCount = 0,
        int EmptyBoxCount = 0)
    {
        public static AreaStats Empty { get; } = new(0, 0, 0, 0, 0);
    }

    private static FarmingAreaDto MapArea(FarmingArea a, string? ownerName = null, AreaStats? stats = null)
    {
        stats ??= AreaStats.Empty;
        return new(
            a.Id, a.OwnerId, ownerName, a.Code, a.Name, a.Location, a.Address, a.Region,
            a.AreaSquareMeters, a.EstablishedAt, a.CreatedAt, a.Description, a.AvatarUrl,
            a.Status.ToString(), a.IsActive, stats.RowCount,
            stats.BoxCount, stats.CrabCount, stats.HealthyBoxCount, stats.AlertBoxCount,
            a.MapImageUrl, a.MapX1, a.MapY1, a.MapX2, a.MapY2,
            stats.OccupiedBoxCount, stats.WatchBoxCount, stats.EmptyBoxCount,
            a.UpdatedAt ?? a.CreatedAt, a.Latitude, a.Longitude);
    }

    /// <summary>Hộp "Theo dõi": status watch/maintenance nhưng chưa tới mức cảnh báo.</summary>
    private static bool IsWatchBox(Box box)
    {
        var status = box.Status ?? "";
        return status.Equals(BoxStatuses.Watch, StringComparison.OrdinalIgnoreCase)
            || status.Equals(BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<Guid, AreaStats>> BuildAreaStatsAsync(
        IEnumerable<Guid> areaIds, CancellationToken ct)
    {
        var ids = areaIds.Where(id => id != Guid.Empty).ToHashSet();
        var result = ids.ToDictionary(id => id, _ => AreaStats.Empty);
        if (ids.Count == 0) return result;

        var rows = (await _uow.FarmingRows.GetAllAsync(ct) ?? Enumerable.Empty<FarmingRow>())
            .Where(r => ids.Contains(r.FarmingAreaId))
            .ToList();
        var rowToArea = rows.ToDictionary(r => r.Id, r => r.FarmingAreaId);
        var rowIds = rowToArea.Keys.ToHashSet();

        var boxes = rowIds.Count == 0
            ? new List<Box>()
            : (await _uow.Boxes.GetAllAsync(ct) ?? Enumerable.Empty<Box>())
                .Where(b => rowIds.Contains(b.FarmingRowId))
                .ToList();

        var boxIds = boxes.Select(b => b.Id).ToHashSet();
        var (crabsByBox, _, _) = boxIds.Count == 0
            ? (
                new Dictionary<Guid, List<Crab>>(),
                new Dictionary<Guid, Guid>(),
                new Dictionary<Guid, DateTime>())
            : await LoadLiveCrabLocationsAsync(ct);

        var waterSystems = (await _uow.WaterSystems.GetAllAsync(ct) ?? Enumerable.Empty<WaterSystem>())
            .Where(w => w.FarmingAreaId is Guid aid && ids.Contains(aid))
            .ToList();
        var wsToArea = waterSystems
            .Where(w => w.FarmingAreaId.HasValue)
            .ToDictionary(w => w.Id, w => w.FarmingAreaId!.Value);
        var sensors = wsToArea.Count == 0
            ? new List<Sensor>()
            : (await _uow.Sensors.GetAllAsync(ct) ?? Enumerable.Empty<Sensor>())
                .Where(s => s.WaterSystemId is Guid wid && wsToArea.ContainsKey(wid))
                .ToList();
        var sensorToArea = sensors
            .Where(s => s.WaterSystemId is Guid wid && wsToArea.ContainsKey(wid))
            .ToDictionary(s => s.Id, s => wsToArea[s.WaterSystemId!.Value]);

        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();
        var areaHasWarning = new HashSet<Guid>();
        foreach (var alert in activeAlerts.Where(a => a.Severity >= AlertSeverity.Warning))
        {
            if (alert.SensorId is Guid sid && sensorToArea.TryGetValue(sid, out var aid))
                areaHasWarning.Add(aid);
        }

        foreach (var areaId in ids)
        {
            var areaBoxes = boxes
                .Where(b => rowToArea.TryGetValue(b.FarmingRowId, out var aid) && aid == areaId)
                .ToList();
            var areaBoxIds = areaBoxes.Select(b => b.Id).ToHashSet();
            var crabCount = areaBoxIds.Sum(id =>
                crabsByBox.TryGetValue(id, out var list) ? list.Count : 0);
            var farmWarning = areaHasWarning.Contains(areaId);
            // Bình thường / Theo dõi / Cảnh báo chỉ tính trên hộp ĐANG CÓ CUA;
            // hộp không có cua = Hộp trống → 4 nhóm cộng lại đúng bằng tổng hộp.
            var occupiedList = areaBoxes
                .Where(box => crabsByBox.TryGetValue(box.Id, out var list) && list.Count > 0)
                .ToList();
            var occupiedBoxes = occupiedList.Count;
            var alertBoxes = occupiedList.Count(box => IsAlertBox(box, farmWarning, activeAlerts));
            var watchBoxes = occupiedList.Count(box =>
                !IsAlertBox(box, farmWarning, activeAlerts) && IsWatchBox(box));
            var emptyBoxes = Math.Max(0, areaBoxes.Count - occupiedBoxes);
            var healthy = Math.Max(0, occupiedBoxes - alertBoxes - watchBoxes);
            var rowCount = rows.Count(r => r.FarmingAreaId == areaId);
            result[areaId] = new AreaStats(
                rowCount, areaBoxes.Count, crabCount, healthy, alertBoxes,
                occupiedBoxes, watchBoxes, emptyBoxes);
        }

        return result;
    }

    private static bool IsAlertBox(Box box, bool farmHasWarning, IEnumerable<Alert> alerts)
    {
        var status = box.Status ?? "";
        if (status.Equals(BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(box.Code)
            && alerts.Any(a => a.Message.Contains(box.Code, StringComparison.OrdinalIgnoreCase)))
            return true;
        return farmHasWarning && box.IsOccupied;
    }

    private static Guid CurrentBoxId(Crab c) => ResolveCurrentBoxId(c);

    private static Guid ResolveCurrentBoxId(
        Crab c, IReadOnlyDictionary<Guid, Guid>? openAllocByCrab = null)
    {
        if (c.BoxId is Guid snap && snap != Guid.Empty)
            return snap;
        if (openAllocByCrab is not null
            && openAllocByCrab.TryGetValue(c.Id, out var fromDb)
            && fromDb != Guid.Empty)
            return fromDb;
        return c.BoxAllocations
            .Where(a => a.EndTime is null)
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault()?.BoxId ?? Guid.Empty;
    }

    /// <summary>Hộp hiện tại, hoặc hộp cuối (allocation đã đóng) để lọc khu.</summary>
    private static Guid LastKnownBoxId(Crab c, BoxContext? ctx = null)
    {
        var current = ResolveCurrentBoxId(c);
        if (current != Guid.Empty) return current;
        if (ctx is not null && ctx.BoxByCrab.TryGetValue(c.Id, out var live) && live != Guid.Empty)
            return live;
        return c.BoxAllocations
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault()?.BoxId ?? Guid.Empty;
    }

    private async Task<string> PeekNextFarmCodeAsync(CancellationToken ct)
    {
        var all = await _uow.FarmingAreas.GetAllAsync(ct);
        return FormatAreaCode(MaxAreaCodeNumber(all) + 1);
    }

    private async Task<string> AllocateAreaCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = await PeekNextFarmCodeAsync(ct);
            if (!await _uow.FarmingAreas.AnyAsync(a => a.Code == code, ct))
                return code;
        }

        throw AppException.Conflict("Could not allocate a unique area code. Retry.");
    }

    private static int MaxAreaCodeNumber(IEnumerable<FarmingArea> areas)
    {
        var max = 0;
        foreach (var area in areas)
        {
            var n = ParseAreaCodeNumber(area.Code);
            if (n > max) max = n;
        }
        return max;
    }

    /// <summary>AREA-A01 → 1, AREA-A99 → 99, AREA-B01 → 100. Also accepts AREA-001 / FARM-001.</summary>
    private static int ParseAreaCodeNumber(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;
        var letter = AreaLetterCodeRegex.Match(code);
        if (letter.Success
            && int.TryParse(letter.Groups[2].Value, out var num)
            && num is >= 1 and <= 99)
        {
            var letterIndex = char.ToUpperInvariant(letter.Groups[1].Value[0]) - 'A';
            return letterIndex * 99 + num;
        }

        var numeric = AreaNumericCodeRegex.Match(code);
        if (numeric.Success && int.TryParse(numeric.Groups[1].Value, out var n))
            return n;

        if (code.StartsWith("FARM-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(code.AsSpan(5), out var farmN))
            return farmN;

        return 0;
    }

    /// <summary>1 → AREA-A01, 99 → AREA-A99, 100 → AREA-B01.</summary>
    private static string FormatAreaCode(int n) => FormatLetterCode("AREA", n);

    private static string FormatLetterCode(string prefix, int n)
    {
        if (n < 1) n = 1;
        var letterIndex = (n - 1) / 99;
        var num = (n - 1) % 99 + 1;
        var letter = (char)('A' + letterIndex);
        return $"{prefix}-{letter}{num.ToString("D2")}";
    }

    private async Task<string> PeekNextRowCodeAsync(CancellationToken ct)
    {
        var all = await _uow.FarmingRows.GetAllAsync(ct);
        return FormatLetterCode("DAY", MaxRowCodeNumber(all) + 1);
    }

    private async Task<string> AllocateRowCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = await PeekNextRowCodeAsync(ct);
            if (!await _uow.FarmingRows.AnyAsync(r => r.Code == code, ct))
                return code;
        }

        throw AppException.Conflict("Could not allocate a unique row code. Retry.");
    }

    private static int MaxRowCodeNumber(IEnumerable<FarmingRow> rows)
    {
        var max = 0;
        foreach (var row in rows)
        {
            var n = ParseLetterCodeNumber(row.Code, "DAY");
            if (n > max) max = n;
        }
        return max;
    }

    private static int ParseLetterCodeNumber(string? code, string prefix)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;
        var expected = prefix + "-";
        if (!code.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || code.Length < expected.Length + 3)
            return 0;
        var tail = code[expected.Length..];
        if (tail.Length == 3 && char.IsLetter(tail[0]) && int.TryParse(tail[1..], out var num) && num is >= 1 and <= 99)
        {
            var letterIndex = char.ToUpperInvariant(tail[0]) - 'A';
            return letterIndex * 99 + num;
        }
        if (int.TryParse(tail, out var n))
            return n;
        return 0;
    }

    private static FarmStatus ParseFarmStatus(string? raw, FarmStatus fallback = FarmStatus.Active)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        return raw.Trim().ToLowerInvariant() switch
        {
            "active" => FarmStatus.Active,
            "suspended" => FarmStatus.Suspended,
            "closed" or "inactive" or "disabled" => FarmStatus.Closed,
            _ => throw AppException.BadRequest("Status must be Active | Suspended | Closed.")
        };
    }

    private static void ApplyStatus(FarmingArea area, FarmStatus status)
    {
        area.Status = status;
        area.IsActive = status == FarmStatus.Active;
    }

    private static void ApplyStatus(FarmingRow row, FarmStatus status)
    {
        row.Status = status;
        row.IsActive = status == FarmStatus.Active;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? NormalizeCoord(double? value, double min, double max, string name)
    {
        if (value is null) return null;
        if (double.IsNaN(value.Value) || value < min || value > max)
            throw AppException.BadRequest($"{name} must be between {min} and {max}.");
        return value;
    }

    private static DateTime? NormalizeDate(DateTime? value)
    {
        if (value is null) return null;
        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Unspecified)
            dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return dt.ToUniversalTime();
    }

    private static FarmingRowDto MapRow(FarmingRow r, FarmingArea? area, AreaStats? stats = null)
    {
        stats ??= AreaStats.Empty;
        return new(
            r.Id, r.FarmingAreaId, area?.Name, area?.Location,
            r.Code, r.Name, r.Location, r.Description,
            r.Capacity, r.SortOrder, r.Status.ToString(), r.IsActive,
            stats.BoxCount, stats.CrabCount, stats.HealthyBoxCount, stats.AlertBoxCount,
            r.MapX, r.MapY,
            stats.OccupiedBoxCount, stats.WatchBoxCount, stats.EmptyBoxCount,
            r.UpdatedAt ?? r.CreatedAt,
            r.CreatedAt);
    }

    private async Task<Dictionary<Guid, AreaStats>> BuildRowStatsAsync(
        IEnumerable<Guid> rowIds, CancellationToken ct)
    {
        var ids = rowIds.Where(id => id != Guid.Empty).ToHashSet();
        var result = ids.ToDictionary(id => id, _ => AreaStats.Empty);
        if (ids.Count == 0) return result;

        var boxes = (await _uow.Boxes.GetAllAsync(ct))
            .Where(b => ids.Contains(b.FarmingRowId))
            .ToList();
        var boxIds = boxes.Select(b => b.Id).ToHashSet();
        var (crabsByBox, _, _) = boxIds.Count == 0
            ? (
                new Dictionary<Guid, List<Crab>>(),
                new Dictionary<Guid, Guid>(),
                new Dictionary<Guid, DateTime>())
            : await LoadLiveCrabLocationsAsync(ct);
        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();

        foreach (var rowId in ids)
        {
            var rowBoxes = boxes.Where(b => b.FarmingRowId == rowId).ToList();
            var rowBoxIds = rowBoxes.Select(b => b.Id).ToHashSet();
            var crabCount = rowBoxIds.Sum(id =>
                crabsByBox.TryGetValue(id, out var list) ? list.Count : 0);
            var occupiedList = rowBoxes
                .Where(box => crabsByBox.TryGetValue(box.Id, out var list) && list.Count > 0)
                .ToList();
            var alertBoxes = occupiedList.Count(box => IsAlertBox(box, false, activeAlerts));
            var watchBoxes = occupiedList.Count(box =>
                !IsAlertBox(box, false, activeAlerts) && IsWatchBox(box));
            var healthy = Math.Max(0, occupiedList.Count - alertBoxes - watchBoxes);
            result[rowId] = new AreaStats(
                0, rowBoxes.Count, crabCount, healthy, alertBoxes,
                occupiedList.Count, watchBoxes, Math.Max(0, rowBoxes.Count - occupiedList.Count));
        }

        return result;
    }

    private static BoxDto MapBox(Box b, BoxContext ctx)
    {
        ctx.Rows.TryGetValue(b.FarmingRowId, out var row);
        FarmingArea? area = null;
        if (row is not null)
            ctx.Areas.TryGetValue(row.FarmingAreaId, out area);

        var crab = PrimaryCrab(ctx, b.Id);
        var crabCount = ctx.CrabsByBox.TryGetValue(b.Id, out var live)
            ? live.Count(IsCrabAlive)
            : 0;
        var occupied = crabCount > 0;
        var condition = MapCrabCondition(crab, occupied);
        var alerts = CountBoxAlerts(b, ctx.ActiveAlerts);
        var status = occupied || KeepBoxStatusWhenEmpty(b.Status)
            ? b.Status
            : BoxStatuses.Empty;

        DateTime? crabInBoxSince = null;
        DateTime? aiUpdatedAt = null;
        DateTime? emptySince = null;
        if (occupied && crab is not null)
        {
            if (ctx.InBoxSinceByCrab.TryGetValue(crab.Id, out var since))
                crabInBoxSince = since;
            else
                crabInBoxSince = crab.UpdatedAt ?? crab.CreatedAt;
            aiUpdatedAt = crab.UpdatedAt ?? crab.CreatedAt;
        }
        else
        {
            emptySince = b.UpdatedAt ?? b.CreatedAt;
        }

        return new BoxDto(
            b.Id,
            b.FarmingRowId,
            row?.FarmingAreaId ?? Guid.Empty,
            row?.Name,
            area?.Name,
            b.Code,
            status,
            occupied,
            BuildBoxDisplayName(b, area),
            area?.Code,
            row?.Code,
            crab?.Id,
            !string.IsNullOrWhiteSpace(crab?.Code) ? crab.Code : crab?.Tag,
            crab?.MoltingStage,
            crab?.Status.ToString(),
            condition,
            alerts,
            BuildAiSummary(occupied, condition, alerts),
            crabCount,
            crabInBoxSince,
            aiUpdatedAt,
            emptySince,
            b.MapX,
            b.MapY);
    }

    private static bool BoxMatchesSearch(Box box, BoxContext ctx, string query)
    {
        if (box.Code.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        ctx.Rows.TryGetValue(box.FarmingRowId, out var row);
        FarmingArea? area = null;
        if (row is not null)
            ctx.Areas.TryGetValue(row.FarmingAreaId, out area);

        if (BuildBoxDisplayName(box, area).Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var crab = PrimaryCrab(ctx, box.Id);
        if (crab is null) return false;
        return (!string.IsNullOrWhiteSpace(crab.Tag)
                && crab.Tag.Contains(query, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(crab.Code)
                && crab.Code.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static Crab? PrimaryCrab(BoxContext ctx, Guid boxId)
    {
        if (!ctx.CrabsByBox.TryGetValue(boxId, out var crabs) || crabs.Count == 0)
            return null;
        return crabs.Where(IsCrabAlive).OrderByDescending(c => c.CreatedAt).FirstOrDefault();
    }

    private static bool KeepBoxStatusWhenEmpty(string? status)
        => string.Equals(status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, BoxStatuses.Watch, StringComparison.OrdinalIgnoreCase);

    private async Task ReleaseCrabFromBoxAsync(Crab crab, string reason, CancellationToken ct)
    {
        var boxId = CurrentBoxId(crab);
        crab.BoxId = null;
        _uow.Crabs.Update(crab);

        var open = await _uow.CrabBoxAllocations.FindAsync(
            a => a.CrabId == crab.Id && a.EndTime == null, ct);
        foreach (var alloc in open)
        {
            alloc.EndTime = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                alloc.Notes = string.IsNullOrWhiteSpace(alloc.Notes)
                    ? reason
                    : alloc.Notes + " | " + reason;
            }
            _uow.CrabBoxAllocations.Update(alloc);
        }

        if (boxId == Guid.Empty)
            return;

        var stillLive = (await _uow.Crabs.FindAsync(
                c => c.BoxId == boxId
                     && c.Id != crab.Id
                     && (c.Status == CrabStatus.Alive
                         || c.Status == CrabStatus.Molting
                         || c.Status == CrabStatus.Quarantined),
                ct))
            .Any();
        if (stillLive)
            return;

        var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
        if (box is null)
            return;

        var oldBoxStatus = box.Status;
        var oldOccupied = box.IsOccupied;
        box.IsOccupied = false;
        box.Status = BoxStatuses.Empty;
        _uow.Boxes.Update(box);

        if (!string.Equals(oldBoxStatus, BoxStatuses.Empty, StringComparison.OrdinalIgnoreCase)
            || oldOccupied)
        {
            await _uow.BoxStatusHistories.AddAsync(new BoxStatusHistory
            {
                BoxId = box.Id,
                OldStatus = oldBoxStatus,
                NewStatus = BoxStatuses.Empty,
                OldIsOccupied = oldOccupied,
                NewIsOccupied = false,
                ChangedAt = DateTime.UtcNow,
                Reason = reason
            }, ct);
        }
    }

    private static string BuildBoxDisplayName(Box box, FarmingArea? area)
    {
        var letter = AreaLetter(area);
        var n = ParseTrailingNumber(box.Code);
        return n > 0 ? $"Hộp {letter}-{n:D3}" : $"Hộp {box.Code}";
    }

    private static string AreaLetter(FarmingArea? area)
    {
        var code = area?.Code ?? "";
        var dash = code.IndexOf('-');
        if (dash >= 0 && dash + 1 < code.Length && char.IsLetter(code[dash + 1]))
            return char.ToUpperInvariant(code[dash + 1]).ToString();

        var name = area?.Name ?? "";
        var ch = name.FirstOrDefault(char.IsLetter);
        return ch == default ? "X" : char.ToUpperInvariant(ch).ToString();
    }

    private static int ParseTrailingNumber(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;
        var i = code.Length - 1;
        while (i >= 0 && char.IsDigit(code[i])) i--;
        return int.TryParse(code[(i + 1)..], out var n) ? n : 0;
    }

    private static string MapCrabCondition(Crab? crab, bool occupied)
    {
        if (crab is null)
            return occupied ? "normal" : "empty";
        var derived = CrabConditions.FromMoltingAndStatus(crab.MoltingStage, crab.Status);
        if (crab.Condition == CrabCondition.Normal && derived != CrabCondition.Normal)
            return CrabConditions.ToApi(derived);
        return CrabConditions.ToApi(crab.Condition);
    }

    private static int CountBoxAlerts(Box box, IEnumerable<Alert> alerts)
    {
        var list = alerts.ToList();
        if (list.Count == 0)
            return IsAlertBox(box, false, list) ? 1 : 0;

        var mentioning = 0;
        if (!string.IsNullOrWhiteSpace(box.Code))
        {
            mentioning = list.Count(a =>
                !string.IsNullOrWhiteSpace(a.Message) &&
                a.Message.Contains(box.Code, StringComparison.OrdinalIgnoreCase));
        }

        if (mentioning > 0) return mentioning;
        return IsAlertBox(box, false, list) ? 1 : 0;
    }

    private static string? BuildAiSummary(bool occupied, string condition, int alertCount)
    {
        if (!occupied && condition == "empty")
            return null;
        if (alertCount > 0)
            return "Cần kiểm tra cảnh báo";
        return condition switch
        {
            "molting" => "Đang lột xác — theo dõi softshell",
            "softshell" => "Cua lột mềm — cửa sổ thu hoạch",
            "premolt" => "Sắp lột — tăng theo dõi",
            "problem" => "AI phát hiện bất thường",
            _ => "Không phát hiện bất thường"
        };
    }

    private static bool IsCrabAlive(Crab c) =>
    c.Status == CrabStatus.Alive
    || c.Status == CrabStatus.Molting
    || c.Status == CrabStatus.Quarantined;

    private static Guid GetCrabBoxId(Crab c) => ResolveCurrentBoxId(c);

    private static CrabDto MapCrab(
        Crab c, BoxContext ctx, CrabLot? lot = null, CrabAiAnalysis? latestAi = null)
    {
        var boxId = ResolveCurrentBoxId(c);
        if (boxId == Guid.Empty && ctx.BoxByCrab.TryGetValue(c.Id, out var liveBox))
            boxId = liveBox;
        if (boxId == Guid.Empty && !IsCrabAlive(c))
            boxId = LastKnownBoxId(c, ctx);

        var box = ctx.Boxes.FirstOrDefault(x => x.Id == boxId);
        FarmingRow? row = null;
        FarmingArea? area = null;
        if (box is not null)
            ctx.Rows.TryGetValue(box.FarmingRowId, out row);
        if (row is not null)
            ctx.Areas.TryGetValue(row.FarmingAreaId, out area);

        // MoltingStage: lấy từ MoltingRecords gần nhất
        var lastMolt = c.MoltingRecords
            .OrderByDescending(m => m.MoltTime)
            .FirstOrDefault();

        // IsAlive: dùng Status enum
        var isAlive = c.Status == CrabStatus.Alive
            || c.Status == CrabStatus.Molting
            || c.Status == CrabStatus.Quarantined;

        return new CrabDto(
            c.Id,
            boxId,
            box?.Code,
            box?.FarmingRowId ?? Guid.Empty,
            row?.FarmingAreaId ?? Guid.Empty,
            c.CrabLotId,
            string.IsNullOrWhiteSpace(c.Tag) ? c.Code : c.Tag,
            c.WeightGram,
            string.IsNullOrWhiteSpace(c.MoltingStage) ? lastMolt?.Result : c.MoltingStage,
            isAlive,
            c.MoltedAt,
            c.StockedAt,
            JsonStringList.Parse(c.ImageUrlsJson),
            c.Code,
            c.QrCode,
            c.CrabType,
            c.Gender.ToString(),
            c.InitialWeightGram,
            c.CarapaceWidthMm,
            c.InitialCondition,
            c.Notes,
            CrabConditions.ToApi(c.Condition),
            c.Status.ToString(),
            c.AiPrediction,
            c.AiConfidence,
            CarapaceLengthMm: c.CarapaceLengthMm,
            RowName: row?.Name,
            RowCode: row?.Code,
            AreaName: area?.Name,
            AreaCode: area?.Code,
            LotCode: lot?.LotCode,
            LotName: lot?.Name,
            ImportDate: lot?.ImportDate,
            AiAnalyzedAt: latestAi?.AnalyzedAt,
            AiRecommendation: FirstNonEmpty(
                latestAi?.AnomalyNote,
                RecommendFromPrediction(c.AiPrediction ?? latestAi?.Prediction)),
            AvatarUrl: JsonStringList.Parse(c.ImageUrlsJson).FirstOrDefault());
    }

    private async Task<IReadOnlyList<CrabTimelineEventDto>> BuildCrabTimelineAsync(
        Crab crab, CrabLot? lot, IReadOnlyList<CrabAiAnalysis> analyses, CancellationToken ct)
    {
        var events = new List<CrabTimelineEventDto>();
        var importAt = lot?.ImportDate ?? crab.StockedAt;
        events.Add(new CrabTimelineEventDto(
            importAt, "stocked", "Nhập hệ thống",
            string.IsNullOrWhiteSpace(lot?.LotCode) ? null : lot!.LotCode));

        var statuses = (await _uow.CrabStatusHistories.FindAsync(h => h.CrabId == crab.Id, ct)).ToList();
        foreach (var h in statuses)
        {
            events.Add(new CrabTimelineEventDto(
                h.ChangedAt, "condition",
                ConditionTimelineTitle(h.NewCondition, h.NewStatus),
                h.Reason));
        }

        foreach (var a in analyses)
        {
            events.Add(new CrabTimelineEventDto(
                a.AnalyzedAt, "ai",
                string.IsNullOrWhiteSpace(a.Prediction) ? "AI phân tích" : $"AI: {a.Prediction}",
                a.AnomalyNote));
        }

        var molts = (await _uow.MoltingRecords.FindAsync(m => m.CrabId == crab.Id, ct)).ToList();
        foreach (var m in molts)
        {
            var ok = string.Equals(m.Result, "success", StringComparison.OrdinalIgnoreCase);
            events.Add(new CrabTimelineEventDto(
                m.MoltTime, "molt",
                ok ? "Đã lột thành công" : $"Lột xác: {m.Result}",
                m.Notes));
        }

        var harvests = (await _uow.CrabHarvestHistories.FindAsync(h => h.CrabId == crab.Id, ct)).ToList();
        foreach (var h in harvests)
        {
            events.Add(new CrabTimelineEventDto(
                h.HarvestedAt, "harvest", "Thu hoạch",
                h.Grade ?? h.Notes));
        }

        var deaths = (await _uow.CrabMortalityRecords.FindAsync(h => h.CrabId == crab.Id, ct)).ToList();
        foreach (var d in deaths)
        {
            events.Add(new CrabTimelineEventDto(
                d.MortalityDate, "mortality", "Ghi nhận chết",
                d.Notes ?? d.Cause.ToString()));
        }

        // Phiếu cho ăn ghi theo cua (FarmOperations.CrabIdsJson — tham chiếu mềm).
        var crabKey = crab.Id.ToString();
        var feedings = (await _uow.FarmOperations.FindAsync(
                o => o.CrabIdsJson.Contains(crabKey), ct))
            .OrderBy(o => o.Timestamp)
            .ToList();
        foreach (var f in feedings)
        {
            var title = string.IsNullOrWhiteSpace(f.Appetite)
                ? "Phiếu chăm sóc"
                : $"Cho ăn — {AppetiteVi(f.Appetite)}";
            events.Add(new CrabTimelineEventDto(
                f.Timestamp, "feeding", title, f.Notes));
        }

        return events.OrderBy(e => e.At).ToList();
    }

    private static string AppetiteVi(string? appetite) => (appetite ?? "").Trim().ToLowerInvariant() switch
    {
        "many" => "ăn nhiều",
        "little" => "ăn ít",
        "none" => "không ăn",
        _ => "chưa ghi"
    };

    private static IReadOnlyList<CrabProfileAlertDto> BuildCrabProfileAlerts(
        Crab crab, CrabAiAnalysis? latestAi, IReadOnlyList<CrabTimelineEventDto> timeline)
    {
        var alerts = new List<CrabProfileAlertDto>();
        if (crab.Condition == CrabCondition.Problem)
        {
            alerts.Add(new CrabProfileAlertDto(
                crab.UpdatedAt ?? crab.CreatedAt, "Cua có vấn đề", crab.Notes, "warning"));
        }
        if (latestAi is not null)
        {
            var activity = latestAi.ActivityLevel ?? "";
            if (activity.Contains("low", StringComparison.OrdinalIgnoreCase)
                || activity.Contains("thấp", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add(new CrabProfileAlertDto(
                    latestAi.AnalyzedAt, "Hoạt động thấp", latestAi.ActivityLevel, "warning"));
            }
            if (!string.IsNullOrWhiteSpace(latestAi.AnomalyNote))
            {
                alerts.Add(new CrabProfileAlertDto(
                    latestAi.AnalyzedAt, "AI phát hiện vấn đề", latestAi.AnomalyNote, "warning"));
            }
        }
        if (crab.Status == CrabStatus.Dead)
        {
            alerts.Add(new CrabProfileAlertDto(
                crab.UpdatedAt ?? crab.CreatedAt, "Cua đã chết", null, "critical"));
        }
        foreach (var e in timeline.Where(t => t.Kind == "molt" && t.Title.Contains("Lột xác:")))
            alerts.Add(new CrabProfileAlertDto(e.At, e.Title, e.Detail, "warning"));
        return alerts.OrderByDescending(a => a.At).ToList();
    }

    private static string ConditionTimelineTitle(CrabCondition condition, CrabStatus status)
    {
        if (status == CrabStatus.Harvested) return "Thu hoạch";
        if (status == CrabStatus.Sold) return "Đã bán";
        if (status == CrabStatus.Dead) return "Đã chết";
        return condition switch
        {
            CrabCondition.Normal => "Bình thường",
            CrabCondition.Premolt => "AI phát hiện sắp lột",
            CrabCondition.Molting => "Phát hiện đang lột",
            CrabCondition.Softshell => "Đã lột — cua mềm",
            CrabCondition.Problem => "Có vấn đề",
            CrabCondition.Weak => "Cua yếu",
            CrabCondition.Dead => "Đã chết",
            CrabCondition.Harvested => "Thu hoạch",
            _ => condition.ToString()
        };
    }

    private static string? RecommendFromPrediction(string? prediction)
    {
        var p = (prediction ?? "").Trim().ToLowerInvariant();
        if (p.Contains("premolt") || p.Contains("sắp lột"))
            return "Theo dõi cua trong 6 giờ tới";
        if (p.Contains("molting") || p.Contains("đang lột"))
            return "Tăng theo dõi softshell, hạn chế tác động";
        if (p.Contains("soft"))
            return "Cửa sổ thu hoạch — kiểm tra mai mềm";
        if (p.Contains("problem") || p.Contains("vấn đề") || p.Contains("dead"))
            return "Kiểm tra hộp và môi trường ngay";
        if (p.Contains("low") || p.Contains("thấp"))
            return "Hoạt động thấp — kiểm tra camera hộp";
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        return null;
    }

    private async Task EnsureLotCapacityAsync(Guid lotId, int adding, CancellationToken ct)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(lotId, ct)
            ?? throw AppException.NotFound("CrabLot");
        if (string.Equals(lot.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Lô nhập đã hủy.");
        var placed = await _uow.Crabs.Query().CountAsync(c => c.CrabLotId == lotId, ct);
        var remaining = lot.Quantity - placed - lot.DeadOnArrival;
        if (remaining < adding)
            throw AppException.BadRequest(
                $"Lô này chỉ còn {Math.Max(0, remaining)} cá thể chưa được tạo.");
    }

    private static bool IsBoxUnusable(string? status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s is "maintenance" or "quarantine" or "harvested" or "suspended" or "closed";
    }

    private async Task<Crab> PersistNewCrabAsync(
        CreateCrabRequest req, Box box, bool autoAssign, CancellationToken ct, string? forcedCode = null)
    {
        var code = string.IsNullOrWhiteSpace(forcedCode)
            ? await AllocateCrabCodeAsync(ct)
            : forcedCode.Trim();
        var condition = string.IsNullOrWhiteSpace(req.Condition)
            ? CrabConditions.FromMoltingAndStatus(req.MoltingStage, CrabStatus.Alive)
            : CrabConditions.Parse(req.Condition);
        var status = CrabConditions.ToLifecycle(condition);
        var initialWeight = req.InitialWeightGram ?? req.WeightGram;

        var crab = new Crab
        {
            BoxId = box.Id,
            CrabLotId = req.CrabLotId,
            Code = code,
            QrCode = $"QR-{code}",
            Tag = string.IsNullOrWhiteSpace(req.Tag) ? code : req.Tag.Trim(),
            CrabType = string.IsNullOrWhiteSpace(req.CrabType) ? null : req.CrabType.Trim(),
            Gender = CrabConditions.ParseGender(req.Gender),
            WeightGram = req.WeightGram,
            InitialWeightGram = initialWeight,
            CarapaceWidthMm = req.CarapaceWidthMm,
            CarapaceLengthMm = req.CarapaceLengthMm,
            InitialCondition = string.IsNullOrWhiteSpace(req.InitialCondition)
                ? "Khỏe mạnh"
                : req.InitialCondition.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            MoltingStage = req.MoltingStage ?? "hard-shell",
            StockedAt = req.StockedAt ?? DateTime.UtcNow,
            Status = status,
            Condition = condition,
            ImageUrlsJson = JsonStringList.Serialize(req.ImageUrls)
        };
        await _uow.Crabs.AddAsync(crab, ct);

        await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
        {
            CrabId = crab.Id,
            NewCondition = condition,
            NewStatus = status,
            ChangedAt = DateTime.UtcNow,
            Source = autoAssign ? "AUTO_ASSIGN" : "MANUAL_ASSIGN",
            Reason = "CRAB_CREATED"
        }, ct);
        if (initialWeight is decimal w)
        {
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = w,
                CarapaceWidthMm = req.CarapaceWidthMm,
                CarapaceLengthMm = req.CarapaceLengthMm,
                MeasuredAt = crab.StockedAt,
                Source = "INITIAL",
                Notes = "Đo lường ban đầu"
            }, ct);
        }

        await _uow.QrCodes.AddAsync(new QrCode
        {
            Code = crab.QrCode!,
            EntityType = "crab",
            CrabId = crab.Id,
            BoxId = box.Id,
            IsActive = true,
            Payload =
                $"{{\"type\":\"crab\",\"crabId\":\"{crab.Id}\",\"crabCode\":\"{code}\",\"crabsense\":\"CRABSENSE:CRAB:{code}\"}}"
        }, ct);

        await _uow.CrabBoxAllocations.AddAsync(new CrabBoxAllocation
        {
            CrabId = crab.Id,
            BoxId = box.Id,
            StartTime = DateTime.UtcNow,
            Notes = autoAssign ? "Auto-assigned empty box" : "Initial placement"
        }, ct);

        box.IsOccupied = true;
        if (string.IsNullOrWhiteSpace(box.Status) || box.Status == "empty")
            box.Status = "active";
        _uow.Boxes.Update(box);
        return crab;
    }

    private async Task<string> AllocateCrabCodeAsync(CancellationToken ct)
        => await CrabCodeAllocator.AllocateAsync(_uow.Crabs, ct);
}
