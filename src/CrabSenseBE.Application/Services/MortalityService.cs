using CrabSenseBE.Application.DTOs.Mortality;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Application.Common;

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

        if (!crab.IsAlive)
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

        crab.IsAlive = false;

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

        return new MortalityRecordDto(
            Id: mortalityRecord.Id,

            CrabId: crab.Id,

            CrabTag: crab.Tag,

            CropBatchId: crab.CropBatchId,

            BatchCode: crab.CropBatch?.BatchCode
                ?? string.Empty,

            BoxId: crab.BoxId,

            BoxCode: crab.Box?.Code
                ?? string.Empty,

            FarmingRowId: crab.Box?.FarmingRowId
                ?? Guid.Empty,

            RowName: crab.Box?.FarmingRow?.Name
                ?? string.Empty,

            FarmingAreaId:
                crab.Box?
                    .FarmingRow?
                    .FarmingAreaId
                ?? Guid.Empty,

            AreaName:
                crab.Box?
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
        var records =await _uow.GetMortalityRecordsWithDetailsAsync(cancellationToken);

        return records
            .Select(x => new MortalityRecordDto(
                Id: x.Id,

                CrabId: x.CrabId,

                CrabTag: x.Crab?.Tag,

                CropBatchId:
                    x.Crab?.CropBatchId
                    ?? Guid.Empty,

                BatchCode:
                    x.Crab?.CropBatch?.BatchCode
                    ?? string.Empty,

                BoxId:
                    x.Crab?.BoxId
                    ?? Guid.Empty,

                BoxCode:
                    x.Crab?.Box?.Code
                    ?? string.Empty,

                FarmingRowId:
                    x.Crab?
                        .Box?
                        .FarmingRowId
                    ?? Guid.Empty,

                RowName:
                    x.Crab?
                        .Box?
                        .FarmingRow?
                        .Name
                    ?? string.Empty,

                FarmingAreaId:
                    x.Crab?
                        .Box?
                        .FarmingRow?
                        .FarmingAreaId
                    ?? Guid.Empty,

                AreaName:
                    x.Crab?
                        .Box?
                        .FarmingRow?
                        .FarmingArea?
                        .Name
                    ?? string.Empty,

                MortalityDate:
                    x.MortalityDate,

                Cause:
                    x.Cause,

                Notes:
                    x.Notes,

                RecordedBy:
                    x.RecordedBy
            ))
            .ToList();
    }
}