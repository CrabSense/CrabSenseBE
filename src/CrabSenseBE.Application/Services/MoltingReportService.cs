using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

public class MoltingReportService : IMoltingReportService
{
    private readonly IUnitOfWork _uow;

    public MoltingReportService(IUnitOfWork uow) => _uow = uow;

    public async Task<MoltingReportDto> GetMoltingReportAsync(
        CancellationToken ct = default)
    {
        // 1. Lấy tất cả cua kèm BoxAllocations → Box → FarmingRow → FarmingArea
        var crabs = await _uow.Crabs
            .Query()
            .Include(c => c.BoxAllocations)
                .ThenInclude(a => a.Box)
                    .ThenInclude(b => b!.FarmingRow)
                        .ThenInclude(r => r!.FarmingArea)
            .ToListAsync(ct);

        // 2. Lấy tất cả MoltingRecord (để xác định cua đã lột)
        var moltedCrabIds = (await _uow.MoltingRecords.GetAllAsync(ct))
            .Select(m => m.CrabId)
            .ToHashSet();

        // 3. Tính toán tổng quan
        var total = crabs.Count;
        var molted = crabs.Count(c => moltedCrabIds.Contains(c.Id));
        var molting = crabs.Count(c => c.Status == CrabStatus.Molting);

        // 4. Thống kê theo khu vực (dùng allocation cuối cùng)
        var byAreas = crabs
            .Select(c => new
            {
                Crab = c,
                LastAllocation = c.BoxAllocations
                    .OrderByDescending(a => a.StartTime)
                    .FirstOrDefault()
            })
            .Where(x => x.LastAllocation?.Box?.FarmingRow?.FarmingArea != null)
            .GroupBy(x => new
            {
                AreaId = x.LastAllocation!.Box!.FarmingRow!.FarmingAreaId,
                AreaName = x.LastAllocation.Box.FarmingRow.FarmingArea!.Name
            })
            .Select(g =>
            {
                var areaCrabs = g.Select(x => x.Crab).ToList();
                var areaTotal = areaCrabs.Count;
                var areaMolted = areaCrabs.Count(c => moltedCrabIds.Contains(c.Id));
                var areaMolting = areaCrabs.Count(c => c.Status == CrabStatus.Molting);

                return new MoltingByAreaDto(
                    g.Key.AreaId, g.Key.AreaName,
                    areaTotal, areaMolting, areaMolted,
                    areaTotal > 0
                        ? decimal.Round((decimal)areaMolted / areaTotal * 100, 2)
                        : 0);
            })
            .OrderBy(x => x.AreaName)
            .ToList();

        // 5. Trả kết quả
        return new MoltingReportDto(
            total, molting, molted,
            total > 0 ? decimal.Round((decimal)molted / total * 100, 2) : 0,
            byAreas);
    }
}