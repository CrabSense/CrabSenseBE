using CrabSenseBE.Application.DTOs.Mortality;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Service xử lý việc ghi nhận cua chết.
/// </summary>
public class MortalityService : IMortalityService
{
    private readonly IUnitOfWork _uow;

    public MortalityService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    /// <summary>
    /// Ghi nhận một cá thể cua chết.
    /// </summary>
    public async Task<MortalityRecordDto> RecordMortalityAsync(
        Guid recordedBy,
        RecordMortalityRequest request,
        CancellationToken cancellationToken = default)
    {
        // ============================================================
        // 1. TÌM CÁ THỂ CUA
        // ============================================================

        var crab = await _uow.GetCrabWithDetailsAsync(
    request.CrabId,
    cancellationToken);

        if (crab is null)
        {
            throw AppException.NotFoundMessage(
                "Không tìm thấy cá thể cua.");
        }

        // ============================================================
        // 2. KIỂM TRA CUA ĐÃ CHẾT HAY CHƯA
        // ============================================================

        if (crab.Status == CrabStatus.Dead || crab.Status == CrabStatus.Harvested || crab.Status == CrabStatus.Missing)
        {
            throw AppException.BadRequest(
                "Cá thể cua này đã được ghi nhận là đã chết trước đó.");
        }

        // ============================================================
        // 3. TẠO BẢN GHI MORTALITY
        // ============================================================

        var mortalityRecord = new CrabMortalityRecord
        {
            CrabId = crab.Id,

            MortalityDate =
                request.MortalityDate
                ?? DateTime.UtcNow,

            Cause = request.Cause,

            Notes = request.Notes,

            RecordedBy = recordedBy
        };

        // ============================================================
        // 4. CẬP NHẬT TRẠNG THÁI CUA + GIẢI PHÓNG HỘP
        // ============================================================

        var oldStatus = crab.Status;
        var oldCondition = crab.Condition;
        var boxId = ResolveBoxId(crab);

        crab.Status = CrabStatus.Dead;
        crab.Condition = CrabCondition.Dead;
        crab.BoxId = null;
        _uow.Crabs.Update(crab);

        await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
        {
            CrabId = crab.Id,
            OldCondition = oldCondition,
            NewCondition = CrabCondition.Dead,
            OldStatus = oldStatus,
            NewStatus = CrabStatus.Dead,
            ChangedAt = DateTime.UtcNow,
            Source = "mortality",
            Reason = request.Notes,
            ChangedByUserId = recordedBy
        }, cancellationToken);

        await ReleaseBoxAsync(crab.Id, boxId, cancellationToken);

        // ============================================================
        // 5. LƯU DATABASE
        // ============================================================

        await _uow.CrabMortalityRecords
            .AddAsync(
                mortalityRecord,
                cancellationToken);

        await _uow.SaveChangesAsync(
            cancellationToken);

        // ============================================================
        // 6. TRẢ KẾT QUẢ
        // ============================================================
var box = boxId != Guid.Empty ? await _uow.Boxes.GetByIdAsync(boxId, cancellationToken) : null;

        return new MortalityRecordDto(
            Id: mortalityRecord.Id,

            CrabId: crab.Id,

            CrabTag: crab.Tag,

            BoxId: boxId,

            BoxCode: box?.Code?? string.Empty,

            FarmingRowId: box?.FarmingRowId?? Guid.Empty,

            RowName: box?.FarmingRow?.Name?? string.Empty,

            FarmingAreaId:
                box?
                    .FarmingRow?
                    .FarmingAreaId
                ?? Guid.Empty,

            AreaName:
                box?
                    .FarmingRow?
                    .FarmingArea?
                    .Name
                ?? string.Empty,

            MortalityDate:
                mortalityRecord.MortalityDate,

            Cause:
                mortalityRecord.Cause,

            Notes:
                mortalityRecord.Notes,

            RecordedBy:
                mortalityRecord.RecordedBy
        );
    }

    /// <summary>
    /// Lấy toàn bộ lịch sử cua chết.
    /// </summary>
    public async Task<IReadOnlyList<MortalityRecordDto>>
        GetMortalityRecordsAsync(
            CancellationToken cancellationToken = default)
    {
        var records =await _uow.CrabMortalityRecords.GetAllAsync(cancellationToken);

        return records.Select(x => {
    var crabTask = _uow.Crabs.GetByIdAsync(x.CrabId, cancellationToken).Result;
    var latestAlloc = crabTask?.BoxAllocations
        .OrderByDescending(a => a.StartTime).FirstOrDefault();
    var boxId = latestAlloc?.BoxId ?? Guid.Empty;
    return new MortalityRecordDto(
        Id: x.Id,
        CrabId: x.CrabId,
        CrabTag: crabTask?.Tag,
        BoxId: boxId,
        BoxCode: "",
        FarmingRowId: Guid.Empty,
        RowName: "",
        FarmingAreaId: Guid.Empty,
        AreaName: "",
        MortalityDate: x.MortalityDate,
        Cause: x.Cause,
        Notes: x.Notes,
        RecordedBy: x.RecordedBy
    );
}).ToList();
    }

    private static Guid ResolveBoxId(Crab crab)
    {
        if (crab.BoxId is Guid boxId && boxId != Guid.Empty)
            return boxId;
        return crab.BoxAllocations
            .OrderByDescending(a => a.StartTime)
            .FirstOrDefault()?.BoxId ?? Guid.Empty;
    }

    private async Task ReleaseBoxAsync(Guid crabId, Guid boxId, CancellationToken cancellationToken)
    {
        var open = await _uow.CrabBoxAllocations.FindAsync(
            a => a.CrabId == crabId && a.EndTime == null,
            cancellationToken);
        foreach (var alloc in open)
        {
            alloc.EndTime = DateTime.UtcNow;
            _uow.CrabBoxAllocations.Update(alloc);
        }

        if (boxId == Guid.Empty)
            return;

        var box = await _uow.Boxes.GetByIdAsync(boxId, cancellationToken);
        if (box is null)
            return;

        var stillLive = (await _uow.Crabs.FindAsync(
                c => c.BoxId == boxId
                     && c.Id != crabId
                     && (c.Status == CrabStatus.Alive
                         || c.Status == CrabStatus.Molting
                         || c.Status == CrabStatus.Quarantined),
                cancellationToken))
            .Any();
        if (stillLive)
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
                Reason = "Crab mortality",
                ChangedByUserId = null
            }, cancellationToken);
        }
    }
}