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
        // 4. CẬP NHẬT TRẠNG THÁI CUA
        // ============================================================

        crab.Status = CrabStatus.Dead;

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
        var latestAlloc = crab.BoxAllocations
    .OrderByDescending(a => a.StartTime)
    .FirstOrDefault();
var boxId = latestAlloc?.BoxId ?? Guid.Empty;
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

    private static bool IsCrabAlive(Crab c) =>
    c.Status == CrabStatus.Alive
    || c.Status == CrabStatus.Molting
    || c.Status == CrabStatus.Quarantined;

    private static Guid GetCrabBoxId(Crab c) =>
        c.BoxAllocations
         .OrderByDescending(a => a.StartTime)
         .FirstOrDefault()?.BoxId ?? Guid.Empty;
}