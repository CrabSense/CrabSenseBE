using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

public class SurvivalRateReportService : ISurvivalRateReportService
{
    private readonly IUnitOfWork _uow;

    public SurvivalRateReportService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<SurvivalRateReportDto> GetSurvivalRateReportAsync(
        CancellationToken cancellationToken = default)
    {
        var crabs = await _uow.Crabs.Query()
            .Include(c => c.CropBatch)
            .Include(c => c.Box)
                .ThenInclude(b => b!.FarmingRow)
                    .ThenInclude(r => r!.FarmingArea)
            .ToListAsync(cancellationToken);

        var total = crabs.Count;
        var alive = crabs.Count(c => c.IsAlive);
        var dead = total - alive;
        var survivalRate = total > 0
            ? decimal.Round((decimal)alive / total * 100, 2)
            : 0;
        var mortalityRate = total > 0
            ? decimal.Round((decimal)dead / total * 100, 2)
            : 0;

        var byCropBatches = crabs
            .GroupBy(c => new { c.CropBatchId, BatchCode = c.CropBatch?.BatchCode ?? "Unknown" })
            .Select(g => new SurvivalByCropBatchDto(
                CropBatchId: g.Key.CropBatchId,
                BatchCode: g.Key.BatchCode,
                TotalCrabs: g.Count(),
                AliveCrabs: g.Count(c => c.IsAlive),
                DeadCrabs: g.Count(c => !c.IsAlive),
                SurvivalRate: g.Count() > 0
                    ? decimal.Round((decimal)g.Count(c => c.IsAlive) / g.Count() * 100, 2)
                    : 0
            ))
            .OrderBy(x => x.BatchCode)
            .ToList();

        var byAreas = crabs
            .GroupBy(c => new
            {
                AreaId = c.Box?.FarmingRow?.FarmingAreaId ?? Guid.Empty,
                AreaName = c.Box?.FarmingRow?.FarmingArea?.Name ?? "Unknown"
            })
            .Select(g => new SurvivalByAreaDto(
                AreaId: g.Key.AreaId,
                AreaName: g.Key.AreaName,
                TotalCrabs: g.Count(),
                AliveCrabs: g.Count(c => c.IsAlive),
                DeadCrabs: g.Count(c => !c.IsAlive),
                SurvivalRate: g.Count() > 0
                    ? decimal.Round((decimal)g.Count(c => c.IsAlive) / g.Count() * 100, 2)
                    : 0
            ))
            .OrderBy(x => x.AreaName)
            .ToList();

        return new SurvivalRateReportDto(
            TotalCrabs: total,
            AliveCrabs: alive,
            DeadCrabs: dead,
            SurvivalRate: survivalRate,
            MortalityRate: mortalityRate,
            ByCropBatches: byCropBatches,
            ByAreas: byAreas
        );
    }

    public async Task<SurvivalByCropBatchDto> GetSurvivalRateByCropBatchAsync(
        Guid cropBatchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await _uow.CropBatches.GetByIdAsync(cropBatchId, cancellationToken);
        if (batch is null)
            throw AppException.BadRequest("Không tìm thấy đợt nuôi.");

        var crabs = await _uow.Crabs.Query()
            .Include(c => c.CropBatch)
            .Where(c => c.CropBatchId == cropBatchId)
            .ToListAsync(cancellationToken);

        var total = crabs.Count;
        var alive = crabs.Count(c => c.IsAlive);
        var dead = total - alive;

        return new SurvivalByCropBatchDto(
            CropBatchId: cropBatchId,
            BatchCode: batch.BatchCode,
            TotalCrabs: total,
            AliveCrabs: alive,
            DeadCrabs: dead,
            SurvivalRate: total > 0
                ? decimal.Round((decimal)alive / total * 100, 2)
                : 0
        );
    }

    public async Task<SurvivalByAreaDto> GetSurvivalRateByAreaAsync(
        Guid areaId,
        CancellationToken cancellationToken = default)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(areaId, cancellationToken);
        if (area is null)
            throw AppException.BadRequest("Không tìm thấy khu vực.");

        var crabs = await _uow.Crabs.Query()
            .Include(c => c.Box)
                .ThenInclude(b => b!.FarmingRow)
                    .ThenInclude(r => r!.FarmingArea)
            .Where(c => c.Box!.FarmingRow!.FarmingAreaId == areaId)
            .ToListAsync(cancellationToken);

        var total = crabs.Count;
        var alive = crabs.Count(c => c.IsAlive);
        var dead = total - alive;

        return new SurvivalByAreaDto(
            AreaId: areaId,
            AreaName: area.Name,
            TotalCrabs: total,
            AliveCrabs: alive,
            DeadCrabs: dead,
            SurvivalRate: total > 0
                ? decimal.Round((decimal)alive / total * 100, 2)
                : 0
        );
    }
}
