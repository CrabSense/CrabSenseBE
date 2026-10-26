using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Mortality;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Service xử lý việc ghi nhận cua chết.
/// Mỗi bản ghi lưu kèm snapshot lúc chết (cân, kích thước mai, hộp)
/// vì cua bị gỡ khỏi hộp và có thể bị sửa dữ liệu sau đó.
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
        // 1. Tìm cá thể cua
        var crab = await _uow.GetCrabWithDetailsAsync(request.CrabId, cancellationToken);

        if (crab is null)
            throw AppException.NotFoundMessage("Không tìm thấy cá thể cua.");

        if (crab.Status is CrabStatus.Dead or CrabStatus.Harvested or CrabStatus.Missing)
            throw AppException.BadRequest("Cá thể cua này đã được ghi nhận là đã chết trước đó.");

        var boxId = ResolveBoxId(crab);

        // 2. Tạo bản ghi mortality kèm snapshot lúc chết.
        //    Client không gửi thì lấy số đo hiện tại của cua.
        var mortalityRecord = new CrabMortalityRecord
        {
            CrabId = crab.Id,
            MortalityDate = request.MortalityDate ?? DateTime.UtcNow,
            Cause = request.Cause,
            Notes = request.Notes,
            RecordedBy = recordedBy,
            WeightGram = request.WeightGram ?? crab.WeightGram,
            CarapaceWidthMm = request.CarapaceWidthMm ?? crab.CarapaceWidthMm,
            CarapaceLengthMm = request.CarapaceLengthMm ?? crab.CarapaceLengthMm,
            BoxId = boxId == Guid.Empty ? null : boxId
        };

        // 3. Cập nhật trạng thái cua + giải phóng hộp
        var oldStatus = crab.Status;
        var oldCondition = crab.Condition;

        crab.Status = CrabStatus.Dead;
        crab.Condition = CrabCondition.Dead;
        crab.BoxId = null;
        if (request.WeightGram is decimal measuredWeight)
            crab.WeightGram = measuredWeight;
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

        // 4. Lưu database
        await _uow.CrabMortalityRecords.AddAsync(mortalityRecord, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        // 5. Trả kết quả
        var box = boxId != Guid.Empty
            ? await _uow.Boxes.GetByIdAsync(boxId, cancellationToken)
            : null;
        var row = box is not null
            ? await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, cancellationToken)
            : null;
        var area = row is not null
            ? await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, cancellationToken)
            : null;

        return new MortalityRecordDto(
            Id: mortalityRecord.Id,
            CrabId: crab.Id,
            CrabTag: crab.Tag,
            BoxId: boxId,
            BoxCode: box?.Code ?? string.Empty,
            FarmingRowId: box?.FarmingRowId ?? Guid.Empty,
            RowName: row?.Name ?? string.Empty,
            FarmingAreaId: row?.FarmingAreaId ?? Guid.Empty,
            AreaName: area?.Name ?? string.Empty,
            MortalityDate: mortalityRecord.MortalityDate,
            Cause: mortalityRecord.Cause,
            Notes: mortalityRecord.Notes,
            RecordedBy: mortalityRecord.RecordedBy,
            WeightGram: mortalityRecord.WeightGram,
            CarapaceWidthMm: mortalityRecord.CarapaceWidthMm,
            CarapaceLengthMm: mortalityRecord.CarapaceLengthMm);
    }

    /// <summary>
    /// Lấy toàn bộ lịch sử cua chết, mới nhất trước.
    /// </summary>
    public async Task<IReadOnlyList<MortalityRecordDto>> GetMortalityRecordsAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _uow.GetMortalityRecordsWithDetailsAsync(cancellationToken);

        return records.Select(x =>
        {
            var box = x.Box;
            var row = box?.FarmingRow;
            return new MortalityRecordDto(
                Id: x.Id,
                CrabId: x.CrabId,
                CrabTag: x.Crab?.Tag,
                BoxId: x.BoxId ?? Guid.Empty,
                BoxCode: box?.Code ?? string.Empty,
                FarmingRowId: box?.FarmingRowId ?? Guid.Empty,
                RowName: row?.Name ?? string.Empty,
                FarmingAreaId: row?.FarmingAreaId ?? Guid.Empty,
                AreaName: row?.FarmingArea?.Name ?? string.Empty,
                MortalityDate: x.MortalityDate,
                Cause: x.Cause,
                Notes: x.Notes,
                RecordedBy: x.RecordedBy,
                WeightGram: x.WeightGram,
                CarapaceWidthMm: x.CarapaceWidthMm,
                CarapaceLengthMm: x.CarapaceLengthMm);
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
