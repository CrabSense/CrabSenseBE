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
            AvatarUrl = NormalizeOptional(req.AvatarUrl)
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

        var uploaded = await _images.UploadAsync(data, fileName, contentType, "farms", ct);
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

    public async Task<ApiResponse> DeleteAreaAsync(Guid id, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);

        var hasRows = await _uow.FarmingRows.AnyAsync(r => r.FarmingAreaId == id, ct);
        if (hasRows)
            throw AppException.Conflict("Cannot delete area that still has rows. Delete rows first.");

        _uow.FarmingAreas.Remove(area);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Deleted.");
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

        var row = new FarmingRow
        {
            FarmingAreaId = area.Id,
            Code = await AllocateRowCodeAsync(ct),
            Name = req.Name.Trim(),
            Location = NormalizeOptional(req.Location),
            Description = NormalizeOptional(req.Description),
            Capacity = req.Capacity,
            SortOrder = req.SortOrder ?? 0
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

        row.Name = req.Name.Trim();
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
                var boxId = GetCrabBoxId(c);
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
        var code = await PeekNextCrabCodeAsync(ct);
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

            box = await PickEmptyBoxAsync(areaId.Value, rowId, ct)
                ?? throw AppException.Conflict(
                    rowId.HasValue
                        ? "No empty box left in this row."
                        : "No empty box left in this area.");
        }
        else
        {
            // Prefer lowest id: BoxId → auto Row + Area
            if (req.BoxId is null || req.BoxId == Guid.Empty)
                throw AppException.BadRequest(
                    "Provide boxId, or set autoAssignEmptyBox=true to pick next empty box.");

            box = await RequireBoxAsync(req.BoxId.Value, ct);
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
            throw AppException.Conflict($"Box '{box.Code}' already has a live crab.");

        if (req.CrabLotId == Guid.Empty)
            throw AppException.BadRequest("CrabLotId is required — crab must belong to a lot.");

        _ = await _uow.CrabLots.GetByIdAsync(req.CrabLotId, ct)
            ?? throw AppException.NotFound("CrabLot");

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
            Source = "system",
            Reason = "Thả nuôi"
        }, ct);
        if (initialWeight is decimal w)
        {
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = w,
                MeasuredAt = crab.StockedAt,
                Source = "stocking",
                Notes = "Trọng lượng ban đầu"
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

    public async Task<ApiResponse<CrabDto>> UpdateCrabAsync(Guid id, UpdateCrabRequest req, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        var oldCondition = crab.Condition;
        var oldStatus = crab.Status;
        var oldWeight = crab.WeightGram;

        crab.WeightGram = req.WeightGram;
        crab.MoltedAt = req.MoltedAt;
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

        if (!req.IsAlive
            && crab.Condition is not CrabCondition.Harvested
            && crab.Condition is not CrabCondition.Sold)
            crab.Condition = CrabCondition.Dead;
        if (req.IsAlive && crab.Condition == CrabCondition.Dead)
            crab.Condition = CrabConditions.FromMoltingAndStatus(crab.MoltingStage, CrabStatus.Alive);

        crab.Status = CrabConditions.ToLifecycle(crab.Condition);
        _uow.Crabs.Update(crab);

        if (oldCondition != crab.Condition || oldStatus != crab.Status)
        {
            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldCondition = oldCondition,
                NewCondition = crab.Condition,
                OldStatus = oldStatus,
                NewStatus = crab.Status,
                ChangedAt = DateTime.UtcNow,
                Source = "manual"
            }, ct);
        }

        if (req.WeightGram is decimal nextWeight && oldWeight != nextWeight)
        {
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = nextWeight,
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
            .Select(h => new CrabWeightHistoryDto(h.Id, h.CrabId, h.WeightGram, h.MeasuredAt, h.Source, h.Notes))
            .ToList();
        return ApiResponse<IReadOnlyList<CrabWeightHistoryDto>>.Ok(rows);
    }

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
            .Select(CurrentBoxId)
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
        public List<Alert> ActiveAlerts { get; init; } = [];
    }

    private async Task<BoxContext> LoadBoxContextAsync(CancellationToken ct)
    {
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).ToList();
        var crabsByBox = crabs
            .Where(IsCrabAlive)
            .Select(c => (BoxId: CurrentBoxId(c), Crab: c))
            .Where(x => x.BoxId != Guid.Empty)
            .GroupBy(x => x.BoxId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Crab).ToList());
        var alerts = (await _uow.Alerts.GetAllAsync(ct))
            .Where(a => a.Status == AlertStatus.Active)
            .ToList();
        return new BoxContext
        {
            Boxes = boxes,
            Rows = rows,
            Areas = areas,
            CrabsByBox = crabsByBox,
            ActiveAlerts = alerts
        };
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

    private sealed record AreaStats(int RowCount, int BoxCount, int CrabCount, int HealthyBoxCount, int AlertBoxCount)
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
            stats.BoxCount, stats.CrabCount, stats.HealthyBoxCount, stats.AlertBoxCount);
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
        var crabs = boxIds.Count == 0
            ? new List<Crab>()
            : (await _uow.Crabs.GetAllAsync(ct) ?? Enumerable.Empty<Crab>())
                .Where(c => IsCrabAlive(c) && CurrentBoxId(c) != Guid.Empty && boxIds.Contains(CurrentBoxId(c)))
                .ToList();

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
            var crabCount = crabs.Count(c => areaBoxIds.Contains(CurrentBoxId(c)));
            var farmWarning = areaHasWarning.Contains(areaId);
            var alertBoxes = areaBoxes.Count(box => IsAlertBox(box, farmWarning, activeAlerts));
            var healthy = Math.Max(0, areaBoxes.Count - alertBoxes);
            var rowCount = rows.Count(r => r.FarmingAreaId == areaId);
            result[areaId] = new AreaStats(rowCount, areaBoxes.Count, crabCount, healthy, alertBoxes);
        }

        return result;
    }

    private static bool IsAlertBox(Box box, bool farmHasWarning, IEnumerable<Alert> alerts)
    {
        var status = box.Status ?? "";
        if (status.Equals(BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase)
            || status.Equals(BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(box.Code)
            && alerts.Any(a => a.Message.Contains(box.Code, StringComparison.OrdinalIgnoreCase)))
            return true;
        return farmHasWarning && box.IsOccupied;
    }

    private static Guid CurrentBoxId(Crab c)
        => c.BoxId is Guid bid && bid != Guid.Empty ? bid : GetCrabBoxId(c);

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
            stats.BoxCount, stats.CrabCount, stats.HealthyBoxCount, stats.AlertBoxCount);
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
        var crabs = boxIds.Count == 0
            ? new List<Crab>()
            : (await _uow.Crabs.GetAllAsync(ct))
                .Where(c => IsCrabAlive(c) && CurrentBoxId(c) != Guid.Empty && boxIds.Contains(CurrentBoxId(c)))
                .ToList();
        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();

        foreach (var rowId in ids)
        {
            var rowBoxes = boxes.Where(b => b.FarmingRowId == rowId).ToList();
            var rowBoxIds = rowBoxes.Select(b => b.Id).ToHashSet();
            var crabCount = crabs.Count(c => rowBoxIds.Contains(CurrentBoxId(c)));
            var alertBoxes = rowBoxes.Count(box => IsAlertBox(box, false, activeAlerts));
            var healthy = Math.Max(0, rowBoxes.Count - alertBoxes);
            result[rowId] = new AreaStats(0, rowBoxes.Count, crabCount, healthy, alertBoxes);
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
        var occupied = crab is not null;
        var condition = MapCrabCondition(crab, occupied);
        var alerts = CountBoxAlerts(b, ctx.ActiveAlerts);
        var status = occupied || KeepBoxStatusWhenEmpty(b.Status)
            ? b.Status
            : BoxStatuses.Empty;

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
            BuildAiSummary(occupied, condition, alerts));
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
        => string.Equals(status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase);

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
            "molting" => "Đang lột — theo dõi softshell",
            "softshell" => "Cua lột mềm — cửa sổ thu hoạch",
            "premolt" => "Sắp lột — tăng theo dõi",
            "problem" => "Phát hiện bất thường",
            _ => "Không có bất thường"
        };
    }

    private static bool IsCrabAlive(Crab c) =>
    c.Status == CrabStatus.Alive
    || c.Status == CrabStatus.Molting
    || c.Status == CrabStatus.Quarantined;

    private static Guid GetCrabBoxId(Crab c) =>
        c.BoxAllocations
         .OrderByDescending(a => a.StartTime)
         .FirstOrDefault()?.BoxId ?? Guid.Empty;
    private static CrabDto MapCrab(
        Crab c, BoxContext ctx, CrabLot? lot = null, CrabAiAnalysis? latestAi = null)
    {
        // Lấy allocation mới nhất (box hiện tại)
        var lastAllocation = c.BoxAllocations
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault();

        var boxId = lastAllocation?.BoxId ?? Guid.Empty;

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

        return events.OrderBy(e => e.At).ToList();
    }

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

    private async Task<string> PeekNextCrabCodeAsync(CancellationToken ct)
    {
        var n = await NextGlobalCrabNumberAsync(ct);
        return $"CRAB-{n:D4}";
    }

    private async Task<string> AllocateCrabCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = await PeekNextCrabCodeAsync(ct);
            if (!await _uow.Crabs.AnyAsync(c => c.Code == code, ct))
                return code;
        }
        throw AppException.Conflict("Could not allocate a unique crab code. Retry.");
    }

    private async Task<int> NextGlobalCrabNumberAsync(CancellationToken ct)
    {
        const string prefix = "CRAB-";
        var crabs = await _uow.Crabs.GetAllAsync(ct);
        var used = new HashSet<int>();
        var max = 0;
        foreach (var c in crabs)
        {
            foreach (var raw in new[] { c.Code, c.Tag })
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = raw[prefix.Length..];
                if (!int.TryParse(suffix, out var n)) continue;
                used.Add(n);
                if (n > max) max = n;
            }
        }

        for (var i = 1; i <= max + 1; i++)
        {
            if (!used.Contains(i))
                return i;
        }

        return max + 1;
    }
}
