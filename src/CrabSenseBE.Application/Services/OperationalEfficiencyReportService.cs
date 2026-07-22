using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

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

        // // ============================================================
        // // 2. QUERY CRABS
        // // ============================================================

        // var crabs = await _uow.Crabs
        //     .Query()
        //     .Where(c => c.StockedAt >= fromDate
        //              && c.StockedAt < toDate)
        //     .ToListAsync(cancellationToken);

        // ============================================================
        // 3. LẤY MOLTING RECORDS
        // ============================================================
        //
        // HashSet chứa Id của mọi cua đã lột.
        // Chỉ cần 1 MoltingRecord là tính là đã lột.
        // ============================================================

        var moltedCrabIds = (await _uow.MoltingRecords
            .GetAllAsync(cancellationToken))
            .Select(m => m.CrabId)
            .ToHashSet();

        // ============================================================
        // 4. TÍNH TOÁN
        // ============================================================

        var total = crabs.Count;

        var molted = crabs
            .Count(c => c.MoltedAt != null);

        var harvested = crabs
            .Count(c => c.Status == CrabStatus.Harvested);

        var alive = crabs
            .Count(c => c.MoltedAt == null 
                    && (c.Status == CrabStatus.Alive
                    || c.Status == CrabStatus.Quarantined));

        var dead = crabs
            .Count(c => c.Status == CrabStatus.Dead);

        // ============================================================
        // 5. TRẢ KẾT QUẢ
        // ============================================================

        return new OperationalEfficiencyDto(
            TotalCrabs: total,
            MoltedCrabs: molted,
            HarvestedCrabs: harvested,
            AliveCrabs: alive,
            DeadCrabs: dead,

            MoltingRate: CalculateRate(molted, total),
            HarvestRate: CalculateRate(harvested, total),
            MortalityRate: CalculateRate(dead, total),
            AliveRate: CalculateRate(alive, total));
    }

    private static decimal CalculateRate(int count, int total)
    {
        return total > 0
            ? decimal.Round((decimal)count / total * 100, 2)
            : 0;
    }
}