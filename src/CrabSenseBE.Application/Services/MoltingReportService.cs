using CrabSenseBE.Application.Common;
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
        return await GetMoltingReportAsync(
            new MoltingReportFilterDto(null, null, null), ct);
    }

    /// <summary>
    /// Báo cáo molting với filter thời gian + trend.
    /// </summary>
    public async Task<MoltingReportDto> GetMoltingReportAsync(
        MoltingReportFilterDto filter,
        CancellationToken ct = default)
    {

        if (filter is null)
        {
            throw AppException.BadRequest(
                "Molting report filter is required.");
        }

        if (filter.FromDate.HasValue &&
            filter.ToDate.HasValue &&
            filter.FromDate.Value.Date >
            filter.ToDate.Value.Date)
        {
            throw AppException.BadRequest(
                "FromDate cannot be greater than ToDate.");
        }
        if (filter.AreaId.HasValue)
        {
            var areaExists = await _uow.FarmingAreas.AnyAsync(
                area => area.Id == filter.AreaId.Value,
                ct);

            if (!areaExists)
            {
                throw AppException.NotFound("Farming area");
            }
        }
        // 1. Lấy tất cả cua kèm allocations
        var crabs = await _uow.Crabs
            .Query()
            .Include(c => c.BoxAllocations)
                .ThenInclude(a => a.Box)
                    .ThenInclude(b => b!.FarmingRow)
                        .ThenInclude(r => r!.FarmingArea)
            .ToListAsync(ct);

        // 2. Lấy MoltingRecords
        var moltingRecords = await _uow.MoltingRecords.GetAllAsync(ct);
        var moltedCrabIds = moltingRecords
            .Select(m => m.CrabId)
            .ToHashSet();

        // 3. Lọc theo khu vực (nếu có)
        if (filter.AreaId.HasValue)
        {
            crabs = crabs.Where(c =>
            {
                var lastAllocation = c.BoxAllocations
                    .OrderByDescending(a => a.StartTime)
                    .FirstOrDefault();
                return lastAllocation?.Box?.FarmingRow?.FarmingAreaId
                    == filter.AreaId.Value;
            }).ToList();
        }

        // 4. Lọc theo thời gian (dựa trên MoltingRecord.MoltedAt hoặc Crab.MoltedAt)
        var filteredCrabs = crabs.AsEnumerable();

        if (filter.FromDate.HasValue)
        {
            var fromDate = filter.FromDate.Value.Date;
            filteredCrabs = filteredCrabs.Where(c =>
                c.MoltedAt.HasValue && c.MoltedAt.Value >= fromDate
                || c.Status == CrabStatus.Molting);
        }

        if (filter.ToDate.HasValue)
        {
            var toDate = filter.ToDate.Value.Date.AddDays(1);
            filteredCrabs = filteredCrabs.Where(c =>
                c.MoltedAt.HasValue && c.MoltedAt.Value < toDate
                || c.Status == CrabStatus.Molting);
        }

        var filtered = filteredCrabs.ToList();

        // 5. Tính toán tổng quan
        var total = filtered.Count;
        var molted = filtered.Count(c => moltedCrabIds.Contains(c.Id));
        var molting = filtered.Count(c => c.Status == CrabStatus.Molting);

        // 6. Thống kê theo khu vực
        var byAreas = filtered
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

        // 7. Trend theo ngày (dựa trên MoltingRecord.MoltedAt)
        var filteredCrabIds = filtered
    .Select(c => c.Id)
    .ToHashSet();

        var successfulRecords = moltingRecords
            .Where(m =>
                filteredCrabIds.Contains(m.CrabId) &&
                string.Equals(
                    m.Result,
                    "success",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var trend = successfulRecords
        .GroupBy(m => m.MoltTime.Date)
        .Select(g =>
        {
            var moltedCount = g
                .Select(m => m.CrabId)
                .Distinct()
                .Count();

            var trendRate = total > 0
                ? decimal.Round(
                    (decimal)moltedCount / total * 100,
                    2)
                : 0;

            return new MoltingTrendDto(
                Date: g.Key,
                MoltingCount: 0,
                MoltedCount: moltedCount,
                MoltingRate: trendRate);
        })
        .OrderBy(x => x.Date)
        .ToList();

        // 8. Trả kết quả
        return new MoltingReportDto(
            total, molting, molted,
            total > 0 ? decimal.Round((decimal)molted / total * 100, 2) : 0,
            byAreas,
            trend);
    }
}