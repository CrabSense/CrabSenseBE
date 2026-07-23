using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Quản lý lô cua (CrabLot) và vụ nuôi (CropBatch).
/// Quantity / AverageWeightGram của lô = hệ thống tính từ cua gắn lô (mọi trạng thái).
/// </summary>
public class FarmLotService : IFarmLotService
{
    private readonly IUnitOfWork _uow;

    public FarmLotService(IUnitOfWork uow) => _uow = uow;

    // ─── CrabLot ────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<CrabLotDto>>> GetLotsAsync(CancellationToken ct = default)
    {
        var lots = await _uow.CrabLots.GetAllAsync(ct);
        var stats = await LoadLotStatsAsync(ct);
        return ApiResponse<IEnumerable<CrabLotDto>>.Ok(lots.Select(l => MapLot(l, stats)));
    }

    public async Task<ApiResponse<CrabLotDto>> GetLotByIdAsync(Guid id, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        var stats = await LoadLotStatsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, stats));
    }

    public async Task<ApiResponse<CrabLotDto>> CreateLotAsync(CreateCrabLotRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.LotCode))
            throw AppException.BadRequest("LotCode is required.");
        if (await _uow.CrabLots.AnyAsync(l => l.LotCode == req.LotCode.Trim(), ct))
            throw AppException.Conflict($"LotCode '{req.LotCode}' already exists.");

        var lot = new CrabLot
        {
            LotCode = req.LotCode.Trim(),
            ImportDate = req.ImportDate == default ? DateTime.UtcNow : req.ImportDate,
            Quantity = 0,
            AverageWeightGram = null,
            SupplierName = req.SupplierName,
            Notes = req.Notes
        };
        await _uow.CrabLots.AddAsync(lot, ct);
        await _uow.SaveChangesAsync(ct);

        var stats = await LoadLotStatsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, stats),
            "Created. Quantity/avg weight auto-update when crabs are placed in this lot.");
    }

    public async Task<ApiResponse<CrabLotDto>> UpdateLotAsync(Guid id, UpdateCrabLotRequest req, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");
        lot.SupplierName = req.SupplierName;
        lot.Notes = req.Notes;
        _uow.CrabLots.Update(lot);
        await _uow.SaveChangesAsync(ct);

        var stats = await LoadLotStatsAsync(ct);
        return ApiResponse<CrabLotDto>.Ok(MapLot(lot, stats));
    }

    public async Task<ApiResponse> DeleteLotAsync(Guid id, CancellationToken ct = default)
    {
        var lot = await _uow.CrabLots.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabLot");

        var inUse = await _uow.Crabs.AnyAsync(c => c.CrabLotId == id, ct);
        if (inUse)
            throw AppException.Conflict(
                $"Cannot delete crab lot '{lot.LotCode}' — crabs still reference it. Soft-delete/remove crabs first.");

        _uow.CrabLots.Remove(lot);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok($"Crab lot '{lot.LotCode}' deleted.");
    }

    // ─── CropBatch ──────────────────────────────────────────────────────────

    // public async Task<ApiResponse<IEnumerable<CropBatchDto>>> GetBatchesAsync(CancellationToken ct = default)
    // {
    //     var batches = await _uow.CropBatches.GetAllAsync(ct);
    //     return ApiResponse<IEnumerable<CropBatchDto>>.Ok(batches.Select(MapBatch));
    // }

    // public async Task<ApiResponse<CropBatchDto>> CreateBatchAsync(CreateCropBatchRequest req, CancellationToken ct = default)
    // {
    //     if (string.IsNullOrWhiteSpace(req.BatchCode))
    //         throw AppException.BadRequest("BatchCode is required.");
    //     if (await _uow.CropBatches.AnyAsync(b => b.BatchCode == req.BatchCode.Trim(), ct))
    //         throw AppException.Conflict($"BatchCode '{req.BatchCode}' already exists.");

    //     var batch = new CropBatch
    //     {
    //         BatchCode = req.BatchCode.Trim(),
    //         StartDate = req.StartDate == default ? DateTime.UtcNow : req.StartDate,
    //         Status = "active",
    //         Notes = req.Notes
    //     };
    //     await _uow.CropBatches.AddAsync(batch, ct);
    //     await _uow.SaveChangesAsync(ct);
    //     return ApiResponse<CropBatchDto>.Ok(MapBatch(batch), "Created.");
    // }

    // public async Task<ApiResponse<CropBatchDto>> UpdateBatchAsync(Guid id, UpdateCropBatchRequest req, CancellationToken ct = default)
    // {
    //     var batch = await _uow.CropBatches.GetByIdAsync(id, ct)
    //         ?? throw AppException.NotFound("CropBatch");
    //     batch.EndDate = req.EndDate ?? batch.EndDate;
    //     batch.Status = req.Status ?? batch.Status;
    //     batch.Notes = req.Notes ?? batch.Notes;
    //     _uow.CropBatches.Update(batch);
    //     await _uow.SaveChangesAsync(ct);
    //     return ApiResponse<CropBatchDto>.Ok(MapBatch(batch));
    // }

    // public async Task<ApiResponse> DeleteBatchAsync(Guid id, CancellationToken ct = default)
    // {
    //     var batch = await _uow.CropBatches.GetByIdAsync(id, ct)
    //         ?? throw AppException.NotFound("CropBatch");

    //     var inUse = await _uow.Crabs.AnyAsync(c => c.CropBatchId == id, ct);
    //     if (inUse)
    //         throw AppException.Conflict(
    //             $"Cannot delete crop batch '{batch.BatchCode}' — crabs still reference it. Soft-delete/remove crabs first.");

    //     _uow.CropBatches.Remove(batch);
    //     await _uow.SaveChangesAsync(ct);
    //     return ApiResponse.Ok($"Crop batch '{batch.BatchCode}' deleted.");
    // }

    private async Task<Dictionary<Guid, LotStats>> LoadLotStatsAsync(CancellationToken ct)
    {
        var crabs = await _uow.Crabs.GetAllAsync(ct);
        return crabs
            .GroupBy(c => c.CrabLotId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var list = g.ToList();
                    var weights = list.Where(c => c.WeightGram.HasValue).Select(c => c.WeightGram!.Value).ToList();
                    return new LotStats(
                        list.Count,
                        weights.Count > 0 ? weights.Average() : null);
                });
    }

    private static CrabLotDto MapLot(CrabLot l, IReadOnlyDictionary<Guid, LotStats> stats)
    {
        stats.TryGetValue(l.Id, out var s);
        return new(
            l.Id,
            l.LotCode,
            l.ImportDate,
            s?.Quantity ?? 0,
            s?.AverageWeightGram,
            l.SupplierName,
            l.Notes);
    }

    // private static CropBatchDto MapBatch(CropBatch b) =>
    //     new(b.Id, b.BatchCode, b.StartDate, b.EndDate, b.Status, b.Notes);

    private sealed record LotStats(int Quantity, decimal? AverageWeightGram);
}
