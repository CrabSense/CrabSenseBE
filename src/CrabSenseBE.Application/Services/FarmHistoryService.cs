using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Application.Services;

/// <summary>
/// Lịch sử nuôi: allocation cua↔box, molting, trạng thái hộp + timeline.
/// </summary>
public class FarmHistoryService : IFarmHistoryService
{
    private readonly IUnitOfWork _uow;

    public FarmHistoryService(IUnitOfWork uow) => _uow = uow;

    // ─── Allocation ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<CrabBoxAllocationDto>> AllocateCrabAsync(
        AllocateCrabRequest req, CancellationToken ct = default)
    {
        if (req.FarmingAreaId == Guid.Empty)
            throw AppException.BadRequest("FarmingAreaId is required.");
        if (req.FarmingRowId == Guid.Empty)
            throw AppException.BadRequest("FarmingRowId is required.");
        if (req.CrabId == Guid.Empty)
            throw AppException.BadRequest("CrabId is required.");
        if (req.BoxId == Guid.Empty)
            throw AppException.BadRequest("BoxId is required — crab must be placed in a box.");

        var crab = await _uow.Crabs.GetByIdAsync(req.CrabId, ct)
            ?? throw AppException.NotFound("Crab");
        if (!IsCrabAlive(crab))
            throw AppException.BadRequest("Cannot allocate a dead/harvested crab.");

        var area = await _uow.FarmingAreas.GetByIdAsync(req.FarmingAreaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        if (!area.IsActive)
            throw AppException.BadRequest($"Farming area '{area.Name}' is inactive.");

        var row = await _uow.FarmingRows.GetByIdAsync(req.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        if (!row.IsActive)
            throw AppException.BadRequest($"Farming row '{row.Name}' is inactive.");
        if (row.FarmingAreaId != area.Id)
            throw AppException.BadRequest("FarmingRowId does not belong to FarmingAreaId.");

        var box = await _uow.Boxes.GetByIdAsync(req.BoxId, ct)
            ?? throw AppException.NotFound("Box");
        if (box.FarmingRowId != row.Id)
            throw AppException.BadRequest("BoxId does not belong to FarmingRowId.");

        var allocsWithCrab = await _uow.CrabBoxAllocations.FindAsync(
    a => a.BoxId == req.BoxId && a.EndTime == null && a.CrabId != req.CrabId, ct);
        var liveInTarget = allocsWithCrab.Any();
        if (liveInTarget)
            throw AppException.Conflict($"Box '{box.Code}' already has a live crab.");

        var open = await _uow.CrabBoxAllocations.FindAsync(
            a => a.CrabId == req.CrabId && a.EndTime == null, ct);
        foreach (var a in open)
        {
            a.EndTime = DateTime.UtcNow;
            _uow.CrabBoxAllocations.Update(a);
        }

        if (GetCrabBoxId(crab) != req.BoxId)
        {
            var prevBox = await _uow.Boxes.GetByIdAsync(GetCrabBoxId(crab), ct);
            if (prevBox is not null)
            {
                var stillAlloc = await _uow.CrabBoxAllocations.FindAsync(
    a => a.BoxId == prevBox.Id && a.EndTime == null && a.CrabId != crab.Id, ct);
                var stillLive = stillAlloc.Any();
                if (!stillLive)
                    await ApplyBoxStatusAsync(prevBox, BoxStatuses.Empty, false, "Crab moved out", ct);
            }
        }

        var alloc = new CrabBoxAllocation
        {
            CrabId = req.CrabId,
            BoxId = req.BoxId,
            StartTime = DateTime.UtcNow,
            Notes = req.Notes
        };
        await _uow.CrabBoxAllocations.AddAsync(alloc, ct);

        _uow.Crabs.Update(crab);

        await ApplyBoxStatusAsync(box, BoxStatuses.Active, true, "Crab allocated", ct);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabBoxAllocationDto>.Ok(MapAlloc(alloc), "Allocated.");
    }

    public async Task<ApiResponse<IEnumerable<CrabBoxAllocationDto>>> GetAllocationsByCrabAsync(
        Guid crabId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var list = await _uow.CrabBoxAllocations.FindAsync(a => a.CrabId == crabId, ct);
        return ApiResponse<IEnumerable<CrabBoxAllocationDto>>.Ok(
            FilterByRange(list, a => a.StartTime, from, to).Select(MapAlloc));
    }

    public async Task<ApiResponse<IEnumerable<CrabBoxAllocationDto>>> GetAllocationsByBoxAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var list = await _uow.CrabBoxAllocations.FindAsync(a => a.BoxId == boxId, ct);
        return ApiResponse<IEnumerable<CrabBoxAllocationDto>>.Ok(
            FilterByRange(list, a => a.StartTime, from, to).Select(MapAlloc));
    }

    public async Task<ApiResponse<CrabBoxAllocationDto>> UpdateAllocationAsync(
        Guid id, UpdateAllocationRequest req, CancellationToken ct = default)
    {
        var a = await _uow.CrabBoxAllocations.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Allocation");

        if (req.StartTime.HasValue)
            a.StartTime = req.StartTime.Value;
        if (req.EndTime.HasValue)
            a.EndTime = req.EndTime.Value;
        // explicit null end = reopen? only set when provided; allow clear via MinValue hack — keep simple: EndTime nullable only update when HasValue
        if (req.Notes is not null)
            a.Notes = req.Notes;

        if (a.EndTime.HasValue && a.EndTime.Value < a.StartTime)
            throw AppException.BadRequest("EndTime must be >= StartTime.");

        _uow.CrabBoxAllocations.Update(a);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabBoxAllocationDto>.Ok(MapAlloc(a), "Allocation updated.");
    }

    public async Task<ApiResponse> DeleteAllocationAsync(Guid id, CancellationToken ct = default)
    {
        var a = await _uow.CrabBoxAllocations.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Allocation");
        if (a.EndTime is null)
            throw AppException.BadRequest(
                "Cannot delete an open allocation (EndTime=null). Close it first or move the crab.");

        _uow.CrabBoxAllocations.Remove(a);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Allocation deleted.");
    }

    // ─── Molting ────────────────────────────────────────────────────────────

    public async Task<ApiResponse<MoltingRecordDto>> CreateMoltingAsync(
        CreateMoltingRecordRequest req, CancellationToken ct = default)
    {
        var crab = await _uow.Crabs.GetByIdAsync(req.CrabId, ct)
            ?? throw AppException.NotFound("Crab");

        var moltTime = req.MoltTime ?? DateTime.UtcNow;
        var result = string.IsNullOrWhiteSpace(req.Result) ? "success" : req.Result!.Trim().ToLowerInvariant();
        var source = string.IsNullOrWhiteSpace(req.Source) ? "manual" : req.Source!.Trim().ToLowerInvariant();
        var allowedResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "success", "failed", "incomplete" };
        if (!allowedResults.Contains(result))
            throw AppException.BadRequest("Result must be success | failed | incomplete.");

        var boxId = req.BoxId ?? GetCrabBoxId(crab);
        if (boxId == Guid.Empty)
            throw AppException.BadRequest("BoxId is required — crab must be in a box to record molting.");

        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");

        var record = new MoltingRecord
        {
            CrabId = req.CrabId,
            BoxId = boxId,
            MoltTime = moltTime,
            WeightAfterGram = req.WeightAfterGram,
            Result = result,
            Source = source,
            Notes = req.Notes
        };
        await _uow.MoltingRecords.AddAsync(record, ct);

        crab.MoltedAt = moltTime;
        if (result == "success")
            crab.MoltingStage = "softshell";
        if (req.WeightAfterGram.HasValue)
            crab.WeightGram = req.WeightAfterGram;
        _uow.Crabs.Update(crab);

        if (result == "success")
            await ApplyBoxStatusAsync(box, BoxStatuses.Molting, box.IsOccupied, "Molting success recorded", ct);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<MoltingRecordDto>.Ok(MapMolt(record), "Molting recorded.");
    }

    public async Task<ApiResponse<IEnumerable<MoltingRecordDto>>> GetMoltingsByCrabAsync(
        Guid crabId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var list = await _uow.MoltingRecords.FindAsync(m => m.CrabId == crabId, ct);
        return ApiResponse<IEnumerable<MoltingRecordDto>>.Ok(
            FilterByRange(list, m => m.MoltTime, from, to).Select(MapMolt));
    }

    public async Task<ApiResponse<IEnumerable<MoltingRecordDto>>> GetMoltingsByBoxAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var list = await _uow.MoltingRecords.FindAsync(m => m.BoxId == boxId, ct);
        return ApiResponse<IEnumerable<MoltingRecordDto>>.Ok(
            FilterByRange(list, m => m.MoltTime, from, to).Select(MapMolt));
    }

    public async Task<ApiResponse<MoltingRecordDto>> UpdateMoltingAsync(
        Guid id, UpdateMoltingRecordRequest req, CancellationToken ct = default)
    {
        var record = await _uow.MoltingRecords.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MoltingRecord");

        if (req.BoxId.HasValue)
        {
            _ = await _uow.Boxes.GetByIdAsync(req.BoxId.Value, ct)
                ?? throw AppException.NotFound("Box");
            record.BoxId = req.BoxId.Value;
        }
        if (req.MoltTime.HasValue)
            record.MoltTime = req.MoltTime.Value;
        if (req.WeightAfterGram.HasValue)
            record.WeightAfterGram = req.WeightAfterGram.Value;
        if (req.Result is not null)
        {
            var result = req.Result.Trim().ToLowerInvariant();
            if (result is not ("success" or "failed" or "incomplete"))
                throw AppException.BadRequest("Result must be success | failed | incomplete.");
            record.Result = result;
        }
        if (req.Source is not null)
            record.Source = string.IsNullOrWhiteSpace(req.Source) ? "manual" : req.Source.Trim().ToLowerInvariant();
        if (req.Notes is not null)
            record.Notes = req.Notes;

        _uow.MoltingRecords.Update(record);
        await ResyncCrabFromMoltingsAsync(record.CrabId, excludeId: null, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<MoltingRecordDto>.Ok(MapMolt(record), "Molting updated.");
    }

    public async Task<ApiResponse> DeleteMoltingAsync(Guid id, CancellationToken ct = default)
    {
        var record = await _uow.MoltingRecords.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("MoltingRecord");
        var crabId = record.CrabId;
        _uow.MoltingRecords.Remove(record);
        await ResyncCrabFromMoltingsAsync(crabId, excludeId: id, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Molting deleted.");
    }

    // ─── Box farming status / history ───────────────────────────────────────

    public async Task<ApiResponse<BoxFarmingStatusDto>> GetBoxFarmingStatusAsync(
        Guid boxId, CancellationToken ct = default)
    {
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        var dto = await BuildFarmingStatusAsync(box, ct);
        return ApiResponse<BoxFarmingStatusDto>.Ok(dto);
    }

    public async Task<ApiResponse<IEnumerable<BoxFarmingStatusDto>>> ListBoxesFarmingStatusAsync(
        Guid? farmingAreaId = null, Guid? farmingRowId = null, string? status = null, CancellationToken ct = default)
    {
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).AsEnumerable();
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);

        if (farmingRowId.HasValue)
            boxes = boxes.Where(b => b.FarmingRowId == farmingRowId.Value);
        if (farmingAreaId.HasValue)
            boxes = boxes.Where(b =>
                rows.TryGetValue(b.FarmingRowId, out var r) && r.FarmingAreaId == farmingAreaId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            boxes = boxes.Where(b => string.Equals(b.Status, st, StringComparison.OrdinalIgnoreCase));
        }

        var list = new List<BoxFarmingStatusDto>();
        foreach (var box in boxes.OrderBy(b => b.Code))
            list.Add(await BuildFarmingStatusAsync(box, ct));

        return ApiResponse<IEnumerable<BoxFarmingStatusDto>>.Ok(list);
    }

    public async Task<ApiResponse<IEnumerable<BoxStatusHistoryDto>>> GetBoxStatusHistoryAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        _ = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");
        var list = await _uow.BoxStatusHistories.FindAsync(h => h.BoxId == boxId, ct);
        return ApiResponse<IEnumerable<BoxStatusHistoryDto>>.Ok(
            FilterByRange(list, h => h.ChangedAt, from, to)
                .Select(MapStatusHist));
    }

    public async Task<ApiResponse<BoxStatusHistoryDto>> UpdateBoxStatusHistoryAsync(
        Guid id, UpdateBoxStatusHistoryRequest req, CancellationToken ct = default)
    {
        var h = await _uow.BoxStatusHistories.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("BoxStatusHistory");

        if (req.OldStatus is not null) h.OldStatus = req.OldStatus;
        if (req.NewStatus is not null)
        {
            if (!BoxStatuses.IsValid(req.NewStatus))
                throw AppException.BadRequest(
                    $"Invalid box status '{req.NewStatus}'. Allowed: {string.Join(", ", BoxStatuses.All)}");
            h.NewStatus = BoxStatuses.Normalize(req.NewStatus);
        }
        if (req.OldIsOccupied.HasValue) h.OldIsOccupied = req.OldIsOccupied;
        if (req.NewIsOccupied.HasValue) h.NewIsOccupied = req.NewIsOccupied.Value;
        if (req.ChangedAt.HasValue) h.ChangedAt = req.ChangedAt.Value;
        if (req.Reason is not null) h.Reason = req.Reason;

        _uow.BoxStatusHistories.Update(h);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<BoxStatusHistoryDto>.Ok(MapStatusHist(h), "Box status history updated.");
    }

    public async Task<ApiResponse> DeleteBoxStatusHistoryAsync(Guid id, CancellationToken ct = default)
    {
        var h = await _uow.BoxStatusHistories.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("BoxStatusHistory");
        _uow.BoxStatusHistories.Remove(h);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Box status history deleted (box current status unchanged).");
    }

    public async Task<ApiResponse<IEnumerable<BoxFarmingEventDto>>> GetBoxFarmingTimelineAsync(
        Guid boxId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        _ = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");

        var events = new List<BoxFarmingEventDto>();

        var allocs = await _uow.CrabBoxAllocations.FindAsync(a => a.BoxId == boxId, ct);
        foreach (var a in FilterByRange(allocs, x => x.StartTime, from, to))
        {
            events.Add(new BoxFarmingEventDto(
                "allocation_start", a.StartTime,
                $"Crab {a.CrabId} entered box", a.CrabId, a.Id));
            if (a.EndTime.HasValue && InRange(a.EndTime.Value, from, to))
            {
                events.Add(new BoxFarmingEventDto(
                    "allocation_end", a.EndTime.Value,
                    $"Crab {a.CrabId} left box", a.CrabId, a.Id));
            }
        }

        var molts = await _uow.MoltingRecords.FindAsync(m => m.BoxId == boxId, ct);
        foreach (var m in FilterByRange(molts, x => x.MoltTime, from, to))
        {
            events.Add(new BoxFarmingEventDto(
                "molting", m.MoltTime,
                $"Molting {m.Result} (source={m.Source})", m.CrabId, m.Id));
        }

        var statuses = await _uow.BoxStatusHistories.FindAsync(h => h.BoxId == boxId, ct);
        foreach (var h in FilterByRange(statuses, x => x.ChangedAt, from, to))
        {
            events.Add(new BoxFarmingEventDto(
                "status", h.ChangedAt,
                $"Status {h.OldStatus ?? "(none)"} → {h.NewStatus}" +
                (string.IsNullOrWhiteSpace(h.Reason) ? "" : $" ({h.Reason})"),
                null, h.Id));
        }

        return ApiResponse<IEnumerable<BoxFarmingEventDto>>.Ok(
            events.OrderByDescending(e => e.At));
    }

    /// <summary>Shared helper — validate + set box status + write history (caller SaveChanges).</summary>
    public async Task ApplyBoxStatusAsync(
        Box box, string newStatus, bool isOccupied, string? reason, CancellationToken ct)
    {
        if (!BoxStatuses.IsValid(newStatus))
            throw AppException.BadRequest(
                $"Invalid box status '{newStatus}'. Allowed: {string.Join(", ", BoxStatuses.All)}");

        var normalized = BoxStatuses.Normalize(newStatus);
        var oldStatus = box.Status;
        var oldOcc = box.IsOccupied;

        if (string.Equals(oldStatus, normalized, StringComparison.OrdinalIgnoreCase)
            && oldOcc == isOccupied)
            return;

        box.Status = normalized;
        box.IsOccupied = isOccupied;
        _uow.Boxes.Update(box);

        await _uow.BoxStatusHistories.AddAsync(new BoxStatusHistory
        {
            BoxId = box.Id,
            OldStatus = oldStatus,
            NewStatus = normalized,
            OldIsOccupied = oldOcc,
            NewIsOccupied = isOccupied,
            ChangedAt = DateTime.UtcNow,
            Reason = reason
        }, ct);
    }

    private async Task<BoxFarmingStatusDto> BuildFarmingStatusAsync(Box box, CancellationToken ct)
    {
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        FarmingArea? area = null;
        if (row is not null)
            area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);

        var allocs = await _uow.CrabBoxAllocations.FindAsync(a => a.BoxId == box.Id && a.EndTime == null, ct);
        var liveCrabIds = allocs.Select(a => a.CrabId).ToList();
        var allCrabs = await _uow.Crabs.FindAsync(c => liveCrabIds.Contains(c.Id), ct);
        var liveCrab = allCrabs.Where(IsCrabAlive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        var openAlloc = (await _uow.CrabBoxAllocations.FindAsync(
                a => a.BoxId == box.Id && a.EndTime == null, ct))
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault();

        var allAlloc = await _uow.CrabBoxAllocations.FindAsync(a => a.BoxId == box.Id, ct);
        var allMolt = await _uow.MoltingRecords.FindAsync(m => m.BoxId == box.Id, ct);
        var lastMolt = allMolt.OrderByDescending(m => m.MoltTime).FirstOrDefault();

        return new BoxFarmingStatusDto(
            box.Id,
            box.Code,
            box.Status,
            box.IsOccupied,
            box.FarmingRowId,
            row?.FarmingAreaId ?? Guid.Empty,
            row?.Name,
            area?.Name,
            liveCrab?.Id,
            liveCrab?.Tag,
            liveCrab?.MoltingStage,
            liveCrab?.WeightGram,
            IsCrabAlive(liveCrab),
            openAlloc?.Id,
            openAlloc?.StartTime,
            allAlloc.Count(),
            allMolt.Count(),
            lastMolt?.MoltTime,
            lastMolt?.Result);
    }

    private async Task ResyncCrabFromMoltingsAsync(Guid crabId, Guid? excludeId, CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
        if (crab is null) return;

        var molts = (await _uow.MoltingRecords.FindAsync(m => m.CrabId == crabId, ct))
            .Where(m => excludeId is null || m.Id != excludeId.Value)
            .OrderByDescending(m => m.MoltTime)
            .ToList();

        var latestSuccess = molts.FirstOrDefault(m =>
            string.Equals(m.Result, "success", StringComparison.OrdinalIgnoreCase));
        var latest = molts.FirstOrDefault();

        if (latestSuccess is not null)
        {
            crab.MoltedAt = latestSuccess.MoltTime;
            crab.MoltingStage = "softshell";
            if (latestSuccess.WeightAfterGram.HasValue)
                crab.WeightGram = latestSuccess.WeightAfterGram;
        }
        else if (latest is not null)
        {
            crab.MoltedAt = latest.MoltTime;
            if (latest.WeightAfterGram.HasValue)
                crab.WeightGram = latest.WeightAfterGram;
        }
        else
        {
            crab.MoltedAt = null;
        }

        _uow.Crabs.Update(crab);
    }

    private static bool IsCrabAlive(Crab c) =>
    c.Status == CrabStatus.Alive
    || c.Status == CrabStatus.Molting
    || c.Status == CrabStatus.Quarantined;

    private static Guid GetCrabBoxId(Crab c) =>
        c.BoxAllocations
         .OrderByDescending(a => a.StartTime)
         .FirstOrDefault()?.BoxId ?? Guid.Empty;

    private static IOrderedEnumerable<T> FilterByRange<T>(
        IEnumerable<T> source, Func<T, DateTime> at, DateTime? from, DateTime? to)
    {
        var q = source.AsEnumerable();
        if (from.HasValue) q = q.Where(x => at(x) >= from.Value);
        if (to.HasValue) q = q.Where(x => at(x) <= to.Value);
        return q.OrderByDescending(at);
    }

    private static bool InRange(DateTime at, DateTime? from, DateTime? to)
    {
        if (from.HasValue && at < from.Value) return false;
        if (to.HasValue && at > to.Value) return false;
        return true;
    }

    private static CrabBoxAllocationDto MapAlloc(CrabBoxAllocation a) =>
        new(a.Id, a.CrabId, a.BoxId, a.StartTime, a.EndTime, a.Notes);

    private static MoltingRecordDto MapMolt(MoltingRecord m) =>
        new(m.Id, m.CrabId, m.BoxId, m.MoltTime, m.WeightAfterGram, m.Result, m.Source, m.Notes);

    private static BoxStatusHistoryDto MapStatusHist(BoxStatusHistory h) =>
        new(h.Id, h.BoxId, h.OldStatus, h.NewStatus,
            h.OldIsOccupied, h.NewIsOccupied, h.ChangedAt, h.Reason);
}
