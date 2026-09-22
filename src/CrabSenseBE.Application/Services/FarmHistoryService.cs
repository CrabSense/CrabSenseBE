using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Media;
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
    private readonly IPublicImageStorage _images;

    public FarmHistoryService(IUnitOfWork uow, IPublicImageStorage images)
    {
        _uow = uow;
        _images = images;
    }

    // ─── Allocation ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<CrabBoxAllocationDto>> AllocateCrabAsync(
        AllocateCrabRequest req, CancellationToken ct = default)
    {
        var boxId = req.DestinationBoxId is Guid dest && dest != Guid.Empty
            ? dest
            : req.BoxId;
        if (boxId == Guid.Empty)
            throw AppException.BadRequest("BoxId / DestinationBoxId is required.");
        if (req.CrabId == Guid.Empty)
            throw AppException.BadRequest("CrabId is required.");

        var crab = await _uow.Crabs.GetByIdAsync(req.CrabId, ct)
            ?? throw AppException.NotFound("Crab");
        if (!IsCrabAlive(crab))
            throw AppException.BadRequest("Cannot allocate a dead/harvested crab.");

        var previousBoxId = GetCrabBoxId(crab);
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct)
            ?? throw AppException.NotFound("FarmingRow");
        if (!row.IsActive)
            throw AppException.BadRequest($"Farming row '{row.Name}' is inactive.");

        var areaId = req.FarmingAreaId is Guid fa && fa != Guid.Empty ? fa : row.FarmingAreaId;
        var rowId = req.FarmingRowId is Guid fr && fr != Guid.Empty ? fr : row.Id;

        if (rowId != row.Id)
            throw AppException.BadRequest("FarmingRowId does not match box.");
        if (areaId != row.FarmingAreaId)
            throw AppException.BadRequest("FarmingAreaId does not match box row.");

        var area = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        if (!area.IsActive)
            throw AppException.BadRequest($"Farming area '{area.Name}' is inactive.");

        if (req.SourceBoxId is Guid src && src != Guid.Empty)
        {
            var current = GetCrabBoxId(crab);
            if (current != Guid.Empty && current != src)
                throw AppException.BadRequest("Crab is not in the given sourceBoxId.");
        }

        var allocsWithCrab = await _uow.CrabBoxAllocations.FindAsync(
            a => a.BoxId == boxId && a.EndTime == null && a.CrabId != req.CrabId, ct);
        if (allocsWithCrab.Any())
            throw AppException.Conflict($"Box '{box.Code}' already has a live crab.");

        var open = await _uow.CrabBoxAllocations.FindAsync(
            a => a.CrabId == req.CrabId && a.EndTime == null, ct);
        foreach (var a in open)
        {
            a.EndTime = DateTime.UtcNow;
            _uow.CrabBoxAllocations.Update(a);
        }

        if (GetCrabBoxId(crab) != boxId)
        {
            var prevBox = await _uow.Boxes.GetByIdAsync(GetCrabBoxId(crab), ct);
            if (prevBox is not null)
            {
                var stillAlloc = await _uow.CrabBoxAllocations.FindAsync(
                    a => a.BoxId == prevBox.Id && a.EndTime == null && a.CrabId != crab.Id, ct);
                if (!stillAlloc.Any())
                    await ApplyBoxStatusAsync(prevBox, BoxStatuses.Empty, false, "Crab moved out", ct);
            }
        }

        var alloc = new CrabBoxAllocation
        {
            CrabId = req.CrabId,
            BoxId = boxId,
            StartTime = DateTime.UtcNow,
            Notes = req.Notes
        };
        await _uow.CrabBoxAllocations.AddAsync(alloc, ct);

        crab.BoxId = boxId;
        crab.BoxAllocations.Add(alloc);
        _uow.Crabs.Update(crab);

        await ApplyBoxStatusAsync(box, BoxStatuses.Active, true, "Crab allocated", ct);

        await _uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = area.OwnerId,
            Action = previousBoxId == Guid.Empty ? "crab_assigned" : "crab_transferred",
            EntityType = "Crab",
            EntityId = crab.Id,
            Details = previousBoxId == Guid.Empty
                ? $"Gán cua {crab.Code} vào hộp {box.Code}"
                : $"Chuyển cua {crab.Code} sang hộp {box.Code}"
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<CrabBoxAllocationDto>.Ok(MapAlloc(alloc), "Allocated.");
    }

    public async Task<ApiResponse<CrabBoxAllocationDto>> TransferCrabAsync(
        MobileTransferCrabRequest req, CancellationToken ct = default)
        => await AllocateCrabAsync(new AllocateCrabRequest(
            req.CrabId,
            req.DestinationBoxId,
            Notes: req.Notes,
            SourceBoxId: req.SourceBoxId,
            DestinationBoxId: req.DestinationBoxId), ct);

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
        var result = NormalizeMoltResult(req.Result);
        var source = string.IsNullOrWhiteSpace(req.Source) ? "manual" : req.Source!.Trim().ToLowerInvariant();

        Guid? boxId = req.BoxId is Guid requested && requested != Guid.Empty
            ? requested
            : GetCrabBoxIdOrNull(crab);
        Box? box = null;
        if (boxId is Guid resolvedBox)
        {
            box = await _uow.Boxes.GetByIdAsync(resolvedBox, ct)
                ?? throw AppException.NotFound("Box");
        }

        var record = new MoltingRecord
        {
            CrabId = req.CrabId,
            BoxId = boxId,
            MoltTime = moltTime,
            WeightAfterGram = req.WeightAfterGram,
            Result = result,
            Source = source,
            Notes = req.Notes,
            PhotoUrlsJson = "[]"
        };
        await _uow.MoltingRecords.AddAsync(record, ct);

        crab.MoltedAt = moltTime;
        if (result == "success")
        {
            crab.MoltingStage = "softshell";
            var oldCondition = crab.Condition;
            var oldStatus = crab.Status;
            crab.Condition = CrabCondition.Softshell;
            crab.Status = CrabConditions.ToLifecycle(crab.Condition);
            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldCondition = oldCondition,
                NewCondition = crab.Condition,
                OldStatus = oldStatus,
                NewStatus = crab.Status,
                ChangedAt = moltTime,
                Source = source,
                Reason = "Molting success"
            }, ct);
        }
        if (req.WeightAfterGram.HasValue)
        {
            crab.WeightGram = req.WeightAfterGram;
            await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
            {
                CrabId = crab.Id,
                WeightGram = req.WeightAfterGram.Value,
                MeasuredAt = moltTime,
                Source = source,
                Notes = "After molt"
            }, ct);
        }
        _uow.Crabs.Update(crab);

        if (result == "success" && box is not null)
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
            record.Result = NormalizeMoltResult(req.Result);
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

    public async Task<ApiResponse<IEnumerable<BoxStatusHistoryDayDto>>> GetDailyBoxStatusHistoryAsync(
        int days = 7, CancellationToken ct = default)
    {
        var dayCount = Math.Clamp(days, 1, 31);
        var today = DateTime.UtcNow.Date;
        var events = (await _uow.BoxStatusHistories.GetAllAsync(ct))
            .Where(h => h.ChangedAt >= today.AddDays(-(dayCount - 1)))
            .OrderByDescending(h => h.ChangedAt)
            .ToList();
        var result = new List<BoxStatusHistoryDayDto>(dayCount);

        for (var offset = 0; offset < dayCount; offset++)
        {
            var date = today.AddDays(-offset);
            var counts = new int[5];
            foreach (var history in events.Where(h => h.ChangedAt.Date == date))
                counts[StatusBucket(history.NewStatus, history.NewIsOccupied)]++;

            result.Add(new BoxStatusHistoryDayDto(DateOnly.FromDateTime(date),
                counts[0], counts[1], counts[2], counts[3], counts[4]));
        }

        return ApiResponse<IEnumerable<BoxStatusHistoryDayDto>>.Ok(
            result.OrderBy(item => item.Date));
    }

    private static int StatusBucket(
        string? status, bool occupied, CrabCondition? condition = null)
    {
        if (!occupied || string.Equals(status, BoxStatuses.Empty, StringComparison.OrdinalIgnoreCase))
            return 4;
        if (condition is CrabCondition.Problem)
            return 3;
        if (condition is CrabCondition.Premolt or CrabCondition.Weak)
            return 1;
        if (condition is CrabCondition.Molting or CrabCondition.Softshell)
            return 2;
        if (string.Equals(status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static int ConditionPriority(CrabCondition? condition) => condition switch
    {
        CrabCondition.Problem => 5,
        CrabCondition.Molting or CrabCondition.Softshell => 4,
        CrabCondition.Premolt or CrabCondition.Weak => 3,
        CrabCondition.Normal => 2,
        _ => 0
    };

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
            crab.Condition = CrabCondition.Softshell;
            crab.Status = CrabConditions.ToLifecycle(crab.Condition);
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

    private static bool IsCrabAlive(Crab? c) =>
        c is not null && (c.Status == CrabStatus.Alive || c.Status == CrabStatus.Molting || c.Status == CrabStatus.Quarantined);

    private static Guid GetCrabBoxId(Crab c) =>
        GetCrabBoxIdOrNull(c) ?? Guid.Empty;

    /// <summary>Box hiện tại: cột Crab.BoxId trước, rồi allocation đang mở (nếu đã load).</summary>
    private static Guid? GetCrabBoxIdOrNull(Crab c)
    {
        if (c.BoxId is Guid snap && snap != Guid.Empty)
            return snap;
        var fromAlloc = c.BoxAllocations
            .Where(a => a.EndTime is null)
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault()?.BoxId;
        return fromAlloc is Guid id && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Desktop gửi normal / weak / needs_watch; API chuẩn là success | failed | incomplete.
    /// </summary>
    internal static string NormalizeMoltResult(string? raw)
    {
        var key = (raw ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        return key switch
        {
            "" or "success" or "normal" or "good" or "ok" or "passed"
                or "binhthuong" => "success",
            "failed" or "fail" or "dead" or "died" or "death" or "thatbai" => "failed",
            "incomplete" or "weak" or "needs_watch" or "needswatch" or "watch"
                or "poor" or "yeu" => "incomplete",
            _ => throw AppException.BadRequest("Result must be success | failed | incomplete.")
        };
    }

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

    public async Task<ApiResponse<IReadOnlyList<CrabImageDto>>> UploadMoltingImagesAsync(
        Guid moltingId,
        IReadOnlyList<CrabImageFile> files,
        Guid? uploadedBy,
        CancellationToken ct = default)
    {
        ImageUploadRules.ValidateBatch(files);
        var record = await _uow.MoltingRecords.GetByIdAsync(moltingId, ct)
            ?? throw AppException.NotFound("MoltingRecord");

        var existing = JsonStringList.Parse(record.PhotoUrlsJson);
        if (existing.Count + files.Count > ImageUploadRules.MaxUrls)
            throw AppException.BadRequest($"A molting record can have at most {ImageUploadRules.MaxUrls} images.");

        var crabPath = await MediaFolderCodeResolver.ResolvePathAsync(_uow, "crab", record.CrabId, ct);
        var folder = MediaFolderPath.Join(
            crabPath, MediaFolderPath.MoltFolder, record.MoltTime.ToString("yyyyMMdd"));

        var results = new List<CrabImageDto>(files.Count);
        var urls = new List<string>(files.Count);

        foreach (var file in files)
        {
            var uploaded = await _images.UploadAsync(
                file.Data, file.FileName, file.ContentType, folder, ct);
            var url = MediaPhotoResolver.PublicUrl(uploaded);

            await _uow.MediaAssets.AddAsync(new MediaAsset
            {
                Category = "image",
                FileName = file.FileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType,
                SizeBytes = uploaded.SizeBytes,
                Provider = _images.ProviderName,
                StorageKey = uploaded.StorageKey,
                WebViewLink = uploaded.WebViewLink,
                WebContentLink = uploaded.WebContentLink,
                ShareLink = url,
                IsShared = true,
                CrabId = record.CrabId,
                RelatedEntityType = "MoltingRecord",
                RelatedEntityId = record.Id,
                Notes = "molting-image",
                UploadedBy = uploadedBy
            }, ct);

            results.Add(new CrabImageDto(url, uploaded.StorageKey, file.FileName, _images.ProviderName, uploaded.SizeBytes));
            urls.Add(url);
        }

        record.PhotoUrlsJson = JsonStringList.Serialize(
            JsonStringList.Merge(record.PhotoUrlsJson, urls, ImageUploadRules.MaxUrls), ImageUploadRules.MaxUrls);
        _uow.MoltingRecords.Update(record);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<IReadOnlyList<CrabImageDto>>.Ok(results, "Uploaded.");
    }

    public async Task<CrabImageContent?> GetMoltingPhotoAsync(Guid moltingId, int index, CancellationToken ct = default)
    {
        if (index < 0) return null;
        var record = await _uow.MoltingRecords.GetByIdAsync(moltingId, ct);
        if (record is null) return null;

        var urls = JsonStringList.Parse(record.PhotoUrlsJson).ToList();
        var assets = (await _uow.MediaAssets.FindAsync(
                m => m.RelatedEntityId == moltingId && m.RelatedEntityType == "MoltingRecord",
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

        return await MediaPhotoResolver.OpenAsync(_images, url, assetMatch, ct);
    }

    private static MoltingRecordDto MapMolt(MoltingRecord m) =>
        new(m.Id, m.CrabId, m.BoxId, m.MoltTime, m.WeightAfterGram, m.Result, m.Source, m.Notes,
            JsonStringList.Parse(m.PhotoUrlsJson));

    private static BoxStatusHistoryDto MapStatusHist(BoxStatusHistory h) =>
        new(h.Id, h.BoxId, h.OldStatus, h.NewStatus,
            h.OldIsOccupied, h.NewIsOccupied, h.ChangedAt, h.Reason);
}
