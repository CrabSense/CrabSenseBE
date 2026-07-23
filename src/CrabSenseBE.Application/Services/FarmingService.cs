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
    private readonly IUnitOfWork _uow;
    private readonly IBoxQrService _boxQr;

    public FarmingService(IUnitOfWork uow, IBoxQrService boxQr)
    {
        _uow = uow;
        _boxQr = boxQr;
    }

    // ─── FarmingArea ────────────────────────────────────────────────────────

    public async Task<ApiResponse<PagedResult<FarmingAreaDto>>> GetAreasAsync(
        FarmingAreaFilter filter, CancellationToken ct = default)
    {
        var all = await _uow.FarmingAreas.GetAllAsync(ct);
        var q = all.AsEnumerable();

        if (filter.IsActive.HasValue)
            q = q.Where(a => a.IsActive == filter.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(a =>
                a.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (a.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var owners = (await _uow.Users.GetAllAsync(ct)).ToDictionary(u => u.Id);
        return ApiResponse<PagedResult<FarmingAreaDto>>.Ok(
            Page(q.OrderBy(a => a.Name), filter.Page, filter.PageSize,
                a => MapArea(a, owners.GetValueOrDefault(a.OwnerId)?.FullName)));
    }

    public async Task<ApiResponse<FarmingAreaDto>> GetAreaByIdAsync(Guid id, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);
        var owner = await _uow.Users.GetByIdAsync(area.OwnerId, ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area, owner?.FullName));
    }

    public async Task<ApiResponse<FarmingAreaDto>> CreateAreaAsync(
        CreateFarmingAreaRequest req, Guid ownerUserId, CancellationToken ct = default)
    {
        if (ownerUserId == Guid.Empty)
            throw AppException.BadRequest("Owner (logged-in user) is required to create an area.");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");

        var owner = await _uow.Users.GetByIdAsync(ownerUserId, ct)
            ?? throw AppException.NotFound("Owner user");
        if (!owner.IsActive)
            throw AppException.BadRequest("Owner user is inactive.");

        var area = new FarmingArea
        {
            OwnerId = owner.Id,
            Name = req.Name.Trim(),
            Description = req.Description
        };
        await _uow.FarmingAreas.AddAsync(area, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area, owner.FullName), "Created.");
    }

    public async Task<ApiResponse<FarmingAreaDto>> UpdateAreaAsync(Guid id, UpdateFarmingAreaRequest req, CancellationToken ct = default)
    {
        var area = await RequireAreaAsync(id, requireActive: false, ct);
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");

        area.Name = req.Name.Trim();
        area.Description = req.Description;
        area.IsActive = req.IsActive;
        _uow.FarmingAreas.Update(area);
        await _uow.SaveChangesAsync(ct);
        var owner = await _uow.Users.GetByIdAsync(area.OwnerId, ct);
        return ApiResponse<FarmingAreaDto>.Ok(MapArea(area, owner?.FullName));
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
        if (filter.IsActive.HasValue)
            q = q.Where(r => r.IsActive == filter.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            q = q.Where(r => r.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return ApiResponse<PagedResult<FarmingRowDto>>.Ok(
            Page(q.OrderBy(r => r.Name), filter.Page, filter.PageSize,
                r => MapRow(r, areas.GetValueOrDefault(r.FarmingAreaId)?.Name)));
    }

    public async Task<ApiResponse<FarmingRowDto>> GetRowByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await RequireRowAsync(id, requireActive: false, ct);
        var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row, area?.Name));
    }

    public async Task<ApiResponse<FarmingRowDto>> CreateRowAsync(CreateFarmingRowRequest req, CancellationToken ct = default)
    {
        if (req.FarmingAreaId == Guid.Empty)
            throw AppException.BadRequest("FarmingAreaId is required — row must belong to an area.");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.Capacity < 0)
            throw AppException.BadRequest("Capacity must be >= 0 (0 = no boxes yet / unlimited).");
        if (req.Capacity > 500)
            throw AppException.BadRequest("Capacity cannot exceed 500 boxes in one create.");

        var area = await RequireAreaAsync(req.FarmingAreaId, requireActive: true, ct);

        var row = new FarmingRow
        {
            FarmingAreaId = area.Id,
            Name = req.Name.Trim(),
            Capacity = req.Capacity
        };
        await _uow.FarmingRows.AddAsync(row, ct);
        await _uow.SaveChangesAsync(ct);

        var createdBoxes = 0;
        // capacity = số hộp: tạo sẵn đúng capacity hộp (1 field, không tách initialBoxCount)
        if (req.Capacity > 0)
        {
            var start = await MaxGlobalBoxNumberAsync(ct) + 1;
            var created = new List<Box>();
            for (var i = 0; i < req.Capacity; i++)
            {
                var box = new Box
                {
                    FarmingRowId = row.Id,
                    Code = $"BOX-{(start + i):D4}",
                    Status = "empty",
                    IsOccupied = false
                };
                await _uow.Boxes.AddAsync(box, ct);
                created.Add(box);
            }
            await _uow.SaveChangesAsync(ct);

            foreach (var b in created)
                await _boxQr.EnsureBoxQrAsync(b.Id, ct);

            createdBoxes = req.Capacity;
        }

        var msg = createdBoxes > 0
            ? $"Created row with {createdBoxes} box(es) (capacity={req.Capacity})."
            : "Created.";
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row, area.Name), msg);
    }

    public async Task<ApiResponse<FarmingRowDto>> UpdateRowAsync(Guid id, UpdateFarmingRowRequest req, CancellationToken ct = default)
    {
        var row = await RequireRowAsync(id, requireActive: false, ct);
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.Capacity < 0)
            throw AppException.BadRequest("Capacity must be >= 0.");

        if (req.Capacity > 0)
        {
            var boxCount = (await _uow.Boxes.FindAsync(b => b.FarmingRowId == id, ct)).Count();
            if (req.Capacity < boxCount)
                throw AppException.BadRequest($"Capacity ({req.Capacity}) cannot be less than current box count ({boxCount}).");
        }

        row.Name = req.Name.Trim();
        row.Capacity = req.Capacity;
        row.IsActive = req.IsActive;
        _uow.FarmingRows.Update(row);
        await _uow.SaveChangesAsync(ct);

        var area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
        return ApiResponse<FarmingRowDto>.Ok(MapRow(row, area?.Name));
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
            q = q.Where(b => b.Code.Contains(code, StringComparison.OrdinalIgnoreCase));
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

    public async Task<ApiResponse<PagedResult<CrabDto>>> GetCrabsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var all = await _uow.Crabs.Query()
                    .Include(c => c.BoxAllocations)
                    .Include(c => c.MoltingRecords)
                    .ToListAsync(ct); var ctx = await LoadBoxContextAsync(ct);
        int? size = pageSize <= 0 ? null : pageSize;
        return ApiResponse<PagedResult<CrabDto>>.Ok(
            Page(all.OrderByDescending(c => c.CreatedAt), page, size, c => MapCrab(c, ctx)));
    }

    public async Task<ApiResponse<CrabDto>> GetCrabByIdAsync(Guid id, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.Query()
            .Include(c => c.BoxAllocations)
            .Include(c => c.MoltingRecords)
            .FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw AppException.NotFound("Crab");
        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<CrabDto>.Ok(MapCrab(crab, ctx));
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

        if (req.WeightGram is < 0)
            throw AppException.BadRequest("WeightGram must be >= 0.");

        var crab = new Crab
        {
            CrabLotId = req.CrabLotId,
            Tag = string.IsNullOrWhiteSpace(req.Tag) ? null : req.Tag.Trim(),
            WeightGram = req.WeightGram,
            MoltingStage = req.MoltingStage,
            StockedAt = DateTime.UtcNow,
            Status = CrabStatus.Alive
        };
        await _uow.Crabs.AddAsync(crab, ct);

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
        crab.WeightGram = req.WeightGram;
        crab.MoltedAt = req.MoltedAt;
        crab.MoltingStage = req.MoltingStage;
        _uow.Crabs.Update(crab);

        // If crab dies / harvest → free box if no other live crabs
        if (!IsCrabAlive(crab))
        {
            var boxId = GetCrabBoxId(crab);
            var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
            if (box is not null)
            {
                var crabsInBox = await _uow.Crabs.Query()
                    .Include(c => c.BoxAllocations)
                    .Where(c => c.BoxAllocations.Any(a => a.BoxId == box.Id && a.EndTime == null))
                    .ToListAsync(ct);
                var stillLive = crabsInBox.Any(c => IsCrabAlive(c) && c.Id != id);
                if (!stillLive)
                {
                    box.IsOccupied = false;
                    box.Status = "empty";
                    _uow.Boxes.Update(box);
                }

                var open = await _uow.CrabBoxAllocations.FindAsync(
                    a => a.CrabId == id && a.EndTime == null, ct);
                foreach (var a in open)
                {
                    a.EndTime = DateTime.UtcNow;
                    _uow.CrabBoxAllocations.Update(a);
                }
            }
        }

        await _uow.SaveChangesAsync(ct);
        var ctx = await LoadBoxContextAsync(ct);
        return ApiResponse<CrabDto>.Ok(MapCrab(crab, ctx));
    }

    public async Task<ApiResponse> DeleteCrabAsync(Guid id, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Crab");
        if (!IsCrabAlive(crab))
            return ApiResponse.Ok("Crab already inactive (soft-deleted).");

        crab.Status = CrabStatus.Dead;

        var boxId = GetCrabBoxId(crab);
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
        if (box is not null)
        {
            var crabsInBox = await _uow.Crabs.Query()
                .Include(c => c.BoxAllocations)
                .Where(c => c.BoxAllocations.Any(a => a.BoxId == box.Id && a.EndTime == null))
                .ToListAsync(ct);
            var stillLive = crabsInBox.Any(c => IsCrabAlive(c) && c.Id != id);
            if (!stillLive)
            {
                var oldStatus = box.Status;
                var oldOcc = box.IsOccupied;
                box.IsOccupied = false;
                box.Status = BoxStatuses.Empty;
                _uow.Boxes.Update(box);
                await _uow.BoxStatusHistories.AddAsync(new BoxStatusHistory
                {
                    BoxId = box.Id,
                    OldStatus = oldStatus,
                    NewStatus = BoxStatuses.Empty,
                    OldIsOccupied = oldOcc,
                    NewIsOccupied = false,
                    ChangedAt = DateTime.UtcNow,
                    Reason = "Crab soft-deleted"
                }, ct);
            }

            var open = await _uow.CrabBoxAllocations.FindAsync(
                a => a.CrabId == id && a.EndTime == null, ct);
            foreach (var a in open)
            {
                a.EndTime = DateTime.UtcNow;
                a.Notes = string.IsNullOrWhiteSpace(a.Notes)
                    ? "Closed by soft-delete"
                    : a.Notes + " | Closed by soft-delete";
                _uow.CrabBoxAllocations.Update(a);
            }
        }

        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok($"Crab soft-deleted (IsAlive=false); box '{box?.Code}' freed if empty.");
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
            .Select(c => GetCrabBoxId(c))
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
    }

    private async Task<BoxContext> LoadBoxContextAsync(CancellationToken ct)
    {
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);
        return new BoxContext { Boxes = boxes, Rows = rows, Areas = areas };
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

    private static FarmingAreaDto MapArea(FarmingArea a, string? ownerName = null) =>
        new(a.Id, a.OwnerId, ownerName, a.Name, a.Description, a.IsActive, a.Rows?.Count ?? 0);

    private static FarmingRowDto MapRow(FarmingRow r, string? areaName) =>
        new(r.Id, r.FarmingAreaId, areaName, r.Name, r.Capacity, r.IsActive, r.Boxes?.Count ?? 0);

    private static BoxDto MapBox(Box b, BoxContext ctx)
    {
        ctx.Rows.TryGetValue(b.FarmingRowId, out var row);
        FarmingArea? area = null;
        if (row is not null)
            ctx.Areas.TryGetValue(row.FarmingAreaId, out area);

        return new BoxDto(
            b.Id,
            b.FarmingRowId,
            row?.FarmingAreaId ?? Guid.Empty,
            row?.Name,
            area?.Name,
            b.Code,
            b.Status,
            b.IsOccupied);
    }

    private static bool IsCrabAlive(Crab c) =>
    c.Status == CrabStatus.Alive
    || c.Status == CrabStatus.Molting
    || c.Status == CrabStatus.Quarantined;

    private static Guid GetCrabBoxId(Crab c) =>
        c.BoxAllocations
         .OrderByDescending(a => a.StartTime)
         .FirstOrDefault()?.BoxId ?? Guid.Empty;
    private static CrabDto MapCrab(Crab c, BoxContext ctx)
    {
        // Lấy allocation mới nhất (box hiện tại)
        var lastAllocation = c.BoxAllocations
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault();

        var boxId = lastAllocation?.BoxId ?? Guid.Empty;

        var box = ctx.Boxes.FirstOrDefault(x => x.Id == boxId);
        FarmingRow? row = null;
        if (box is not null)
            ctx.Rows.TryGetValue(box.FarmingRowId, out row);

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
            c.Tag,
            c.WeightGram,
            lastMolt?.Result,   // MoltingStage
            isAlive,            // IsAlive
            c.MoltedAt,
            c.StockedAt);
    }
}
