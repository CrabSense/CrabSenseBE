using CrabSenseBE.Application.DTOs.Dashboard;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Dashboard tổng quan trang trại.
///
/// Aggregate data từ nhiều repository:
/// - FarmingAreas, FarmingRows, Boxes, Crabs
/// - CrabMortalityRecords
/// - HarvestVouchers
/// - FrozenLots
/// - Alerts
///
/// Charts:
/// - HarvestTrend: 30 ngày, group by tuần
/// - MortalityTrend: 30 ngày, group by tuần
/// - BoxUtilizationByArea: tỷ lệ lấp đầy theo khu vực
/// - MortalityByCause: nguyên nhân chết
///
/// Alive (đang nuôi):
///     MoltedAt == null && (Alive || Quarantined)
///     Không bao gồm Molting.
///
/// DateTime filter:
///     Dùng in-memory filter để tránh
///     PostgreSQL DateTime Kind mismatch.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;

    public DashboardService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<DashboardDto> GetDashboardOverviewAsync(
        CancellationToken ct = default)
    {
        // ============================================================
        // 1. LOAD TẤT CẢ DATA CẦN THIẾT
        // ============================================================
        // Load 1 lần, dùng In-Memory filter
        // để tránh DateTime Kind mismatch.
        // ============================================================

        var areas = await _uow.FarmingAreas.Query().ToListAsync(ct);
        var rows = await _uow.FarmingRows.Query().ToListAsync(ct);
        var boxes = await _uow.Boxes.Query().ToListAsync(ct);
        var crabLots = await _uow.CrabLots.Query().ToListAsync(ct);

        var crabs = await _uow.Crabs
            .Query()
            .Include(c => c.BoxAllocations)
            .Include(c => c.MoltingRecords)
            .ToListAsync(ct);

        var mortalityRecords =
            await _uow.CrabMortalityRecords.Query().ToListAsync(ct);;

        var harvestVouchers =
            await _uow.HarvestVouchers.Query().ToListAsync(ct);;

        var frozenLots =
            await _uow.FrozenLots.Query().ToListAsync(ct);;

        var alerts =
            await _uow.Alerts.Query().ToListAsync(ct);

        var boxesWithDetails = await _uow.Boxes
            .Query()
            .Include(b => b.FarmingRow)
                .ThenInclude(r => r!.FarmingArea)
            .ToListAsync(ct);

        // ============================================================
        // 2. FARM SUMMARY
        // ============================================================

        var farmSummary = new FarmSummaryDto(
            TotalAreas: areas.Count,
            TotalRows: rows.Count,
            TotalBoxes: boxes.Count,
            TotalCrabs: crabs.Count,
            TotalCrabLots: crabLots.Count);

        // ============================================================
        // 3. BOX UTILIZATION
        // ============================================================

        var occupied = boxes.Count(b => b.IsOccupied);
        var empty = boxes.Count - occupied;

        var boxUtilization = new BoxUtilizationDto(
            Occupied: occupied,
            Empty: empty,
            UtilizationRate: CalculateRate(occupied, boxes.Count));

        // ============================================================
        // 4. CRAB STATUS (loại trừ nhau)
        // ============================================================
        //
        // Alive:   MoltedAt == null && (Alive || Quarantined)
        // Molting: Status == Molting && MoltedAt == null
        // Molted:  MoltedAt != null
        // Harvested: Status == Harvested
        // Dead:    Status == Dead
        // ============================================================

        var alive = crabs.Count(c =>
            c.MoltedAt == null
            && (c.Status == CrabStatus.Alive
                || c.Status == CrabStatus.Quarantined));

        var molting = crabs.Count(c =>
            c.MoltedAt == null
            && c.Status == CrabStatus.Molting);

        var molted = crabs.Count(c =>
            c.MoltedAt != null);

        var harvested = crabs.Count(c =>
            c.Status == CrabStatus.Harvested);

        var dead = crabs.Count(c =>
            c.Status == CrabStatus.Dead);

        var crabStatus = new CrabStatusDto(
            alive, molting, molted, harvested, dead);

        // ============================================================
        // 5. RECENT ACTIVITY
        // ============================================================
        //
        // DeathsLast7Days:
        //     CrabMortalityRecords có MortalityDate >= 7 ngày trước.
        //
        // HarvestsLast7Days:
        //     HarvestVouchers có HarvestDate >= 7 ngày trước
        //     và Status == Completed.
        //
        // ActiveAlerts:
        //     Alerts có Status == Active.
        // ============================================================

        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);

        var deathsLast7Days = mortalityRecords
            .Count(m => m.MortalityDate >= sevenDaysAgo);

        var harvestsLast7Days = harvestVouchers
            .Count(v => v.HarvestDate >= sevenDaysAgo
                     && v.Status == HarvestStatus.Completed);

        var activeAlerts = alerts
            .Count(a => a.Status == AlertStatus.Active);

        var recentActivity = new RecentActivityDto(
            deathsLast7Days,
            harvestsLast7Days,
            activeAlerts);

        // ============================================================
        // 6. FROZEN INVENTORY SUMMARY
        // ============================================================
        //
        // ExpiringSoon:
        //     ExpiryDate <= now + 30 ngày
        //     và ExpiryDate > now (chưa hết hạn).
        // ============================================================

        var expiryThreshold = now.AddDays(30);

        var totalFrozenLots = frozenLots.Count;
        var totalFrozenWeight = frozenLots
            .Sum(l => l.WeightKg);
        var expiringSoon = frozenLots
            .Count(l => l.ExpiryDate <= expiryThreshold
                     && l.ExpiryDate > now);

        var frozenInventory = new FrozenInventorySummaryDto(
            totalFrozenLots,
            totalFrozenWeight,
            expiringSoon);

        // ============================================================
        // 7. CHARTS
        // ============================================================

        // ── 7a. Harvest Trend (30 ngày, group by tuần) ──────────

        var thirtyDaysAgo = now.AddDays(-30);

        var harvestTrend = harvestVouchers
            .Where(v => v.HarvestDate >= thirtyDaysAgo
                     && v.Status == HarvestStatus.Completed)
            .GroupBy(v => GetWeekStart(v.HarvestDate))
            .OrderBy(g => g.Key)
            .Select(g => new HarvestTrendPointDto(
                Date: g.Key,
                Quantity: g.Sum(v => v.TotalQuantity),
                WeightKg: g.Sum(v => v.TotalWeightKg)))
            .ToList();

        // ── 7b. Mortality Trend (30 ngày, group by tuần) ────────

        var mortalityTrend = mortalityRecords
            .Where(m => m.MortalityDate >= thirtyDaysAgo)
            .GroupBy(m => GetWeekStart(m.MortalityDate))
            .OrderBy(g => g.Key)
            .Select(g => new MortalityTrendPointDto(
                Date: g.Key,
                Count: g.Count()))
            .ToList();

        // ── 7c. Box Utilization By Area ─────────────────────────

        var boxUtilByArea = boxesWithDetails
            .Where(b => b.FarmingRow?.FarmingArea != null)
            .GroupBy(b => new
            {
                AreaId = b.FarmingRow!.FarmingAreaId,
                AreaName = b.FarmingRow.FarmingArea!.Name
            })
            .OrderBy(g => g.Key.AreaName)
            .Select(g => new BoxUtilizationByAreaDto(
                AreaId: g.Key.AreaId,
                AreaName: g.Key.AreaName,
                Occupied: g.Count(b => b.IsOccupied),
                Empty: g.Count(b => !b.IsOccupied)))
            .ToList();

        // ── 7d. Mortality By Cause ──────────────────────────────

        var mortalityByCause = mortalityRecords
            .GroupBy(m => m.Cause)
            .OrderByDescending(g => g.Count())
            .Select(g => new MortalityByCauseDto(
                Cause: g.Key.ToString(),
                Count: g.Count()))
            .ToList();

        var charts = new DashboardChartsDto(
            harvestTrend,
            mortalityTrend,
            boxUtilByArea,
            mortalityByCause);

        // ============================================================
        // 8. TRẢ KẾT QUẢ
        // ============================================================

        return new DashboardDto(
            farmSummary,
            boxUtilization,
            crabStatus,
            recentActivity,
            frozenInventory,
            charts);
    }

    // ================================================================
    // HÀM TÍNH TỶ LỆ PHẦN TRĂM
    // ================================================================

    private static decimal CalculateRate(int count, int total)
    {
        return total > 0
            ? decimal.Round((decimal)count / total * 100, 2)
            : 0;
    }

    // ================================================================
    // LẤY NGÀY BẮT ĐẦU TUẦN (THỨ HAI)
    // ================================================================
    //
    // Dùng để group dữ liệu theo tuần.
    // Monday = start of week.
    // ================================================================

    private static DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}