using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Application.Services;
/// <summary>
/// Service tạo báo cáo tỷ lệ sống, chết và thu hoạch của cua.
///
/// Quy tắc nghiệp vụ:
///
/// Alive:
///     - Alive
///     - Molting
///     - Quarantined
///
/// Dead:
///     - Dead
///
/// Harvested:
///     - Harvested
///
/// Tổng số cua:
///     TotalCrabs = Alive + Dead + Harvested
///
/// SurvivalRate:
///     Alive / Total * 100
///
/// MortalityRate:
///     Dead / Total * 100
///
/// HarvestRate:
///     Harvested / Total * 100
///
/// Thời gian lọc:
///     Dựa trên thời điểm thả cua (StockedAt).
///
/// Khu vực:
///     Dựa trên lần phân bổ cuối cùng của cua
///     trong CrabBoxAllocation.
/// </summary>
public class SurvivalRateReportService
    : ISurvivalRateReportService
{
    private readonly IUnitOfWork _uow;

    public SurvivalRateReportService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    /// <summary>
    /// Lấy báo cáo tổng quan tỷ lệ sống của cua.
    ///
    /// Có thể lọc theo:
    /// - Khoảng thời gian thả cua
    /// - Khu vực nuôi
    /// </summary>
    public async Task<SurvivalRateReportDto>
        GetSurvivalRateReportAsync(
            SurvivalRateFilterDto filter,
            CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            throw AppException.BadRequest(
                "Survival rate filter is required.");
        }

        if (filter.FromDate.HasValue &&
            filter.ToDate.HasValue &&
            filter.FromDate.Value.Date > filter.ToDate.Value.Date)
        {
            throw AppException.BadRequest(
                "FromDate cannot be greater than ToDate.");
        }
        // ============================================================
        // 1. KIỂM TRA KHU VỰC NẾU CÓ FILTER
        // ============================================================

        if (filter.AreaId.HasValue)
        {
            var areaExists =
                await _uow.FarmingAreas.AnyAsync(
                    x => x.Id == filter.AreaId.Value,
                    cancellationToken);

            if (!areaExists)
            {
                throw AppException.NotFound(
                    "Không tìm thấy khu vực.");
            }
        }

        // ============================================================
        // 2. XÂY DỰNG QUERY LẤY DỮ LIỆU CUA
        // ============================================================
        //
        // Cần lấy:
        //
        // Crab
        //   └── BoxAllocations
        //         └── Box
        //              └── FarmingRow
        //                    └── FarmingArea
        //
        // Vì cua có thể đã:
        // - Di chuyển sang box khác
        // - Chết
        // - Được thu hoạch
        //
        // nên cần lịch sử phân bổ để xác định
        // khu vực cuối cùng của cua.
        // ============================================================

        var query = _uow.Crabs
            .Query()
            .Include(c => c.BoxAllocations)
                .ThenInclude(a => a.Box)
                    .ThenInclude(b => b!.FarmingRow)
                        .ThenInclude(r => r!.FarmingArea)

            .AsQueryable();

        // ============================================================
        // 3. LỌC THEO THỜI ĐIỂM THẢ CUA
        // ============================================================
        //
        // FromDate:
        //     Lấy từ đầu ngày.
        //
        // ToDate:
        //     Dùng ngày kế tiếp làm mốc kết thúc
        //     để bao gồm toàn bộ ngày ToDate.
        //
        // Ví dụ:
        //
        // FromDate = 01/07/2026
        // ToDate   = 21/07/2026
        //
        // Kết quả:
        //
        // StockedAt >= 01/07/2026 00:00:00
        // StockedAt <  22/07/2026 00:00:00
        //
        // Như vậy toàn bộ ngày 21/07 được tính.
        // ============================================================

        if (filter.FromDate.HasValue)
        {
            var fromDate =
                filter.FromDate.Value.Date;

            query = query.Where(c =>
                c.StockedAt >= fromDate);
        }

        if (filter.ToDate.HasValue)
        {
            var toDateExclusive =
                filter.ToDate.Value
                    .Date
                    .AddDays(1);

            query = query.Where(c =>
                c.StockedAt < toDateExclusive);
        }

        // ============================================================
        // 4. LẤY DỮ LIỆU TỪ DATABASE
        // ============================================================

        var crabs = await query
            .ToListAsync(cancellationToken);

        // ============================================================
        // 5. LỌC THEO KHU VỰC
        // ============================================================
        //
        // Mỗi con cua có thể có nhiều allocation:
        //
        // Allocation 1:
        //     BOX-0001
        //     01/07 → 05/07
        //
        // Allocation 2:
        //     BOX-0002
        //     05/07 → hiện tại
        //
        // Vì vậy lấy allocation có StartTime mới nhất.
        //
        // Đối với:
        // - Cua còn sống:
        //     Đây thường là allocation hiện tại.
        //
        // - Cua đã chết:
        //     Đây là box cuối cùng trước khi chết.
        //
        // - Cua đã thu hoạch:
        //     Đây là box cuối cùng trước khi thu hoạch.
        // ============================================================

        if (filter.AreaId.HasValue)
        {
            crabs = crabs
                .Where(c =>
                {
                    var lastAllocation =
                        c.BoxAllocations
                            .OrderByDescending(
                                a => a.StartTime)
                            .FirstOrDefault();

                    return lastAllocation?
                        .Box?
                        .FarmingRow?
                        .FarmingAreaId
                        == filter.AreaId.Value;
                })
                .ToList();
        }

        // ============================================================
        // 6. TÍNH TỔNG QUAN
        // ============================================================

        var total =
            crabs.Count;

        // ============================================================
        // CUA ĐƯỢC TÍNH LÀ CÒN SỐNG
        // ============================================================
        //
        // Alive:
        //     Cua đang sống bình thường.
        //
        // Molting:
        //     Cua đang trong quá trình lột xác.
        //
        // Quarantined:
        //     Cua đang được cách ly nhưng vẫn còn sống.
        // ============================================================

        var alive =
            crabs.Count(c =>
                c.Status == CrabStatus.Alive
                || c.Status == CrabStatus.Molting
                || c.Status == CrabStatus.Quarantined);

        // ============================================================
        // CUA ĐÃ CHẾT
        // ============================================================

        var dead =
            crabs.Count(c =>
                c.Status == CrabStatus.Dead);

        // ============================================================
        // CUA ĐÃ THU HOẠCH
        // ============================================================

        var harvested =
            crabs.Count(c =>
                c.Status == CrabStatus.Harvested
                || c.Status == CrabStatus.Sold);

        var missing =
            crabs.Count(c =>
                c.Status == CrabStatus.Missing);

        // ============================================================
        // 7. THỐNG KÊ THEO KHU VỰC
        // ============================================================
        //
        // Mỗi cua được xác định khu vực dựa trên
        // allocation cuối cùng của nó.
        // ============================================================

        var byAreas = crabs

            // Lấy allocation cuối cùng của từng con cua
            .Select(c => new
            {
                Crab = c,

                LastAllocation =
                    c.BoxAllocations
                        .OrderByDescending(
                            a => a.StartTime)
                        .FirstOrDefault()
            })

            // Chỉ lấy cua có thông tin khu vực hợp lệ
            .Where(x =>
                x.LastAllocation != null
                && x.LastAllocation.Box != null
                && x.LastAllocation.Box.FarmingRow != null
                && x.LastAllocation.Box.FarmingRow.FarmingArea != null)

            // Nhóm theo khu vực
            .GroupBy(x => new
            {
                AreaId =
                    x.LastAllocation!
                        .Box!
                        .FarmingRow!
                        .FarmingAreaId,

                AreaName =
                    x.LastAllocation!
                        .Box!
                        .FarmingRow!
                        .FarmingArea!
                        .Name
            })

            // Tính thống kê cho từng khu vực
            .Select(g =>
            {
                var areaCrabs =
                    g.Select(x => x.Crab)
                        .ToList();

                var areaTotal =
                    areaCrabs.Count;

                var areaAlive =
                    areaCrabs.Count(c =>
                        c.Status == CrabStatus.Alive
                        || c.Status == CrabStatus.Molting
                        || c.Status == CrabStatus.Quarantined);

                var areaDead =
                    areaCrabs.Count(c =>
                        c.Status == CrabStatus.Dead);

                var areaHarvested =
                    areaCrabs.Count(c =>
                        c.Status == CrabStatus.Harvested
                        || c.Status == CrabStatus.Sold);

                var areaMissing =
                    areaCrabs.Count(c =>
                        c.Status == CrabStatus.Missing);

                return new SurvivalByAreaDto(
                    AreaId:
                        g.Key.AreaId,

                    AreaName:
                        g.Key.AreaName,

                    TotalCrabs:
                        areaTotal,

                    AliveCrabs:
                        areaAlive,

                    DeadCrabs:
                        areaDead,

                    HarvestedCrabs:
                        areaHarvested,

                    SurvivalRate:
                        CalculateRate(
                            areaAlive,
                            areaTotal),

                    MortalityRate:
                        CalculateRate(
                            areaDead,
                            areaTotal),

                    HarvestRate:
                        CalculateRate(
                            areaHarvested,
                            areaTotal)
                );
            })

            // Sắp xếp khu vực theo tên
            .OrderBy(x => x.AreaName)

            .ToList();

        // ============================================================
        // 8. TREND THEO NGÀY (dựa trên StockedAt)
        // ============================================================
        //
        // Nếu filter có ngày: gom theo ngày trong khoảng.
        // Nếu không: gom theo ngày của toàn bộ dữ liệu.
        // ============================================================
        var trend = crabs
            .GroupBy(c => c.StockedAt.Date)
            .Select(g =>
            {
                var groupCrabs = g.ToList();
                var gTotal = groupCrabs.Count;
                var gAlive = groupCrabs.Count(c =>
                    c.Status == CrabStatus.Alive
                    || c.Status == CrabStatus.Molting
                    || c.Status == CrabStatus.Quarantined);
                var gDead = groupCrabs.Count(c =>
                    c.Status == CrabStatus.Dead);
                var gHarvested = groupCrabs.Count(c =>
    c.Status == CrabStatus.Harvested
    || c.Status == CrabStatus.Sold);

                var gMissing = groupCrabs.Count(c =>
                    c.Status == CrabStatus.Missing);

                return new SurvivalTrendDto(
                    g.Key,
                    gTotal, gAlive, gDead, gHarvested,
                    CalculateRate(gAlive, gTotal));
            })
            .OrderBy(x => x.Date)
            .ToList();

        // ============================================================
        // 9. TRẢ KẾT QUẢ
        // ============================================================
        return new SurvivalRateReportDto(
            TotalCrabs: total,
            AliveCrabs: alive,
            DeadCrabs: dead,
            HarvestedCrabs: harvested,
            SurvivalRate: CalculateRate(alive, total),
            MortalityRate: CalculateRate(dead, total),
            HarvestRate: CalculateRate(harvested, total),
            ByAreas: byAreas,
            Trend: trend);
    }

    // ================================================================
    // HÀM TÍNH TỶ LỆ PHẦN TRĂM
    // ================================================================
    //
    // Công thức:
    //
    // Rate = Count / Total × 100
    //
    // Làm tròn 2 chữ số thập phân.
    //
    // Nếu Total = 0:
    //     Trả về 0 để tránh chia cho 0.
    // ================================================================

    private static decimal CalculateRate(
        int count,
        int total)
    {
        return total > 0
            ? decimal.Round(
                (decimal)count / total * 100, 2) : 0;
    }
}