using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Báo cáo hiệu quả vận hành tổng hợp.
///
/// Mặc định lọc theo tháng hiện tại.
/// Công thức:
///     MoltingRate   = MoltedCrabs    / TotalCrabs × 100
///     HarvestRate   = HarvestedCrabs / TotalCrabs × 100
///     MortalityRate = DeadCrabs      / TotalCrabs × 100
///     AliveRate     = AliveCrabs     / TotalCrabs × 100
///
/// AliveCrabs (đang nuôi):
///     Alive + Quarantined
///
/// Thời gian lọc:
///     Dựa trên thời điểm thả cua (StockedAt).
///     Mặc định: tháng hiện tại.
/// </summary>
public class OperationalEfficiencyReportService
    : IOperationalEfficiencyReportService
{
    private readonly IUnitOfWork _uow;

    public OperationalEfficiencyReportService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<OperationalEfficiencyDto>
        GetOperationalEfficiencyReportAsync(
            OperationalEfficiencyFilterDto filter,
            CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            throw AppException.BadRequest(
                "Operational efficiency filter is required.");
        }

        if (filter.FromDate.HasValue &&
            filter.ToDate.HasValue &&
            filter.FromDate.Value.Date > filter.ToDate.Value.Date)
        {
            throw AppException.BadRequest(
                "FromDate cannot be greater than ToDate.");
        }
        // ============================================================
        // 1. XÁC ĐỊNH KHOẢNG THỜI GIAN
        // ============================================================
        //
        // Mặc định: tháng hiện tại.
        //
        // Nếu người dùng truyền FromDate/ToDate:
        //     Dùng khoảng người dùng chọn.
        // ============================================================

        var fromDate = filter.FromDate?.Date
    ?? new DateTime(
        DateTime.UtcNow.Year,
        DateTime.UtcNow.Month,
        1);

        var toDate = filter.ToDate?.Date.AddDays(1)
            ?? fromDate.AddMonths(1);

        // ============================================================
        // 2. LOAD ALL CRABS + FILTER IN MEMORY
        // ============================================================
        // Tránh DateTime Kind mismatch giữa
        // parameter (Unspecified) và DB column (Utc).
        // ============================================================

        var allCrabs = await _uow.Crabs
            .Query()
            .ToListAsync(cancellationToken);

        var crabs = allCrabs
            .Where(c => c.StockedAt >= fromDate
                     && c.StockedAt < toDate)
            .ToList();

        // ============================================================
        // 3. PHÂN LOẠI MUTUALLY EXCLUSIVE
        // ============================================================
        //
        // Mỗi con cua thuộc 1 nhóm duy nhất:
        //
        // Alive:     Status=Alive/Quarantined, MoltedAt=null
        // Molting:   Status=Molting
        // Molted:    MoltedAt != null, Status != Harvested/Dead
        // Harvested: Status=Harvested
        // Dead:      Status=Dead
        // ============================================================

        var alive = crabs.Count(c =>
            c.MoltedAt == null
            && (c.Status == CrabStatus.Alive
                || c.Status == CrabStatus.Quarantined));

        var molting = crabs.Count(c =>
            c.Status == CrabStatus.Molting);

        var molted = crabs.Count(c =>
    c.MoltedAt != null
    && c.Status != CrabStatus.Molting
    && c.Status != CrabStatus.Harvested
    && c.Status != CrabStatus.Dead
    && c.Status != CrabStatus.Missing
    && c.Status != CrabStatus.Sold);

        var harvested = crabs.Count(c =>
            c.Status == CrabStatus.Harvested
            || c.Status == CrabStatus.Sold);

        var dead = crabs.Count(c =>
            c.Status == CrabStatus.Dead);

        var total = crabs.Count;

        // ============================================================
        // 4. TÍNH TỶ LỆ
        // ============================================================

        var survivalRate = CalculateRate(alive + molting, total);
        var moltingRate = CalculateRate(molting, total);
        var harvestRate = CalculateRate(harvested, total);
        var mortalityRate = CalculateRate(dead, total);
        var aliveRate = CalculateRate(alive, total);

        //debug
        var classified =
    alive +
    molting +
    molted +
    harvested +
    dead;

        var unclassified = total - classified;

        // ============================================================
        // 5. TÍNH HEALTH SCORE (composite)
        // ============================================================
        //
        // Công thức:
        //   HealthScore = SurvivalRate × 0.4
        //               + (100 - MortalityRate) × 0.3
        //               + HarvestRate × 0.2
        //               + MoltingRate × 0.1
        //
        // MoltingRate có trọng số thấp vì lột là tự nhiên,
        // không xấu cũng không tốt — chỉ cho thấy hoạt động.
        // ============================================================

        var healthScore = (int)Math.Round(
            survivalRate * 0.4m
            + (100 - mortalityRate) * 0.3m
            + harvestRate * 0.2m
            + moltingRate * 0.1m);

        healthScore = Math.Clamp(healthScore, 0, 100);

        // ============================================================
        // 6. TRẢ KẾT QUẢ
        // ============================================================

        return new OperationalEfficiencyDto(
            TotalCrabs: total,
            AliveCrabs: alive,
            MoltingCrabs: molting,
            MoltedCrabs: molted,
            HarvestedCrabs: harvested,
            DeadCrabs: dead,
            UnclassifiedCrabs: unclassified,


            SurvivalRate: survivalRate,
            MoltingRate: moltingRate,
            HarvestRate: harvestRate,
            MortalityRate: mortalityRate,
            AliveRate: aliveRate,

            HealthScore: healthScore);
    }

    private static decimal CalculateRate(int count, int total)
    {
        return total > 0
            ? decimal.Round((decimal)count / total * 100, 2)
            : 0;
    }
}