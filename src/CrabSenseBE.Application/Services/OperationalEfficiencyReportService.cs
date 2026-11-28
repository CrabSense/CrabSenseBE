using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Current recorded status of crabs stocked during the selected UTC calendar
/// period, scoped by current assigned box; not a historical as-of report.
/// Rates use the same nonempty cohort denominator. No inferred health score.
/// </summary>
public class OperationalEfficiencyReportService
    : IOperationalEfficiencyReportService
{
    private readonly IUnitOfWork _uow;
    private readonly ReportAreaGate _areas;

    public OperationalEfficiencyReportService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new ReportAreaGate(uow, currentUser);
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

        if (filter.FarmingAreaId == Guid.Empty)
            throw AppException.BadRequest("FarmingAreaId must not be empty.");

        var boxIds = await _areas.BoxIdsAsync(filter.FarmingAreaId, cancellationToken);

        if (filter.CrabLotId.HasValue)
        {
            var lotExists = await _uow.CrabLots.AnyAsync(
                lot => lot.Id == filter.CrabLotId.Value,
                cancellationToken);

            if (!lotExists)
            {
                throw AppException.BadRequest("Crab lot does not exist.");
            }
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

        if (fromDate >= toDate)
            throw AppException.BadRequest("Invalid effective date range.");

        var source = _uow.Crabs.Query();
        var allCrabs = source is null
            ? await _uow.Crabs.GetAllAsync(cancellationToken)
            : source.Provider is Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
                ? await source.ToListAsync(cancellationToken)
                : source.ToList();

        var crabs = allCrabs
            .Where(c => c.StockedAt >= fromDate
                     && c.StockedAt < toDate)
            .Where(c => !filter.CrabLotId.HasValue
             || c.CrabLotId == filter.CrabLotId.Value)
            .Where(c => boxIds is null || (c.BoxId is Guid boxId && boxIds.Contains(boxId)))
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
            && (c.Status == CrabStatus.Alive || c.Status == CrabStatus.Quarantined));

        var harvested = crabs.Count(c =>
            c.Status == CrabStatus.Harvested
            || c.Status == CrabStatus.Sold);

        var dead = crabs.Count(c =>
            c.Status == CrabStatus.Dead);

        var total = crabs.Count;

        // ============================================================
        // 4. TÍNH TỶ LỆ
        // ============================================================

        var survivalRate = CalculateRate(alive + molting + molted, total);
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

            HealthScore: null,
            FarmingAreaId: filter.FarmingAreaId,
            CohortBasis: "stockedAt-current-status-v1");
    }

    private static decimal? CalculateRate(int count, int total)
    {
        return total > 0
            ? decimal.Round((decimal)count / total * 100, 2)
            : null;
    }
}