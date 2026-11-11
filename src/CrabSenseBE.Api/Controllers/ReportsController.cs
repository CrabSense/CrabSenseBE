using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = AppRoles.FarmManage + "," + AppRoles.FarmManager)]
public class ReportsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    private readonly ReportAreaGate _areas;
    private readonly IHarvestReportService _harvestReportService;
    private readonly IInventoryReportService _inventoryReportService;
    private readonly ISurvivalRateReportService _survivalRateReportService;
    private readonly IMoltingReportService _moltingReportService;
    private readonly IOperationalEfficiencyReportService _operationalEfficiencyReportService;

    public ReportsController(
        IUnitOfWork uow,
        ICurrentUserService currentUser,
        IHarvestReportService harvestReportService,
        IInventoryReportService inventoryReportService,
        ISurvivalRateReportService survivalRateReportService,
        IMoltingReportService moltingReportService,
        IOperationalEfficiencyReportService operationalEfficiencyReportService)
    {
        _harvestReportService = harvestReportService;
        _inventoryReportService = inventoryReportService;
        _survivalRateReportService = survivalRateReportService;
        _moltingReportService = moltingReportService;
        _operationalEfficiencyReportService = operationalEfficiencyReportService;
        _uow = uow;
        _areas = new ReportAreaGate(uow, currentUser);
    }

    /// <summary>
    /// So sánh các Lô cua (Crab Lot): số con, còn sống, đã thu hoạch, khối lượng thu hoạch.
    /// </summary>
    [HttpGet("crab-lots/compare")]
    public async Task<IActionResult> CompareCrabLots(
        [FromQuery] Guid? farmingAreaId,
        CancellationToken cancellationToken)
    {
        var boxIds = await _areas.BoxIdsAsync(farmingAreaId, cancellationToken);
        var lots = (await _uow.CrabLots.GetAllAsync(cancellationToken)).ToList();
        var crabs = (await _uow.Crabs.GetAllAsync(cancellationToken)).ToList();
        if (boxIds is not null)
            crabs = crabs.Where(crab => crab.BoxId is Guid boxId && boxIds.Contains(boxId)).ToList();

        var lines = await _uow.HarvestLines.GetAllAsync(cancellationToken);
        var weightByCrab = lines
            .Where(line => line.CrabId is Guid)
            .GroupBy(line => line.CrabId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.WeightGram));

        var rows = lots
            .Select(lot =>
            {
                var inLot = crabs.Where(crab => crab.CrabLotId == lot.Id).ToList();
                var harvested = inLot.Where(crab => crab.Status == CrabStatus.Harvested || crab.Status == CrabStatus.Sold).ToList();
                var ended = lot.Status is "Completed" or "Cancelled";
                return new CrabLotCompareRow(
                    lot.Id,
                    lot.LotCode,
                    lot.Name,
                    lot.ImportDate,
                    ended ? lot.UpdatedAt ?? lot.CreatedAt : null,
                    lot.Quantity,
                    inLot.Count,
                    inLot.Count(crab => crab.Status is CrabStatus.Alive or CrabStatus.Molting),
                    inLot.Count(crab => crab.MoltedAt is null),
                    harvested.Count,
                    harvested.Sum(crab => weightByCrab.GetValueOrDefault(crab.Id)) / 1000m);
            })
            .Where(row => boxIds is null || row.CrabCount > 0)
            .OrderBy(row => row.LotCode)
            .ToList();

        return Ok(new { success = true, data = rows });
    }

    /// <summary>
    /// Báo cáo tỉ lệ thu hoạch.
    /// </summary>
    [HttpGet("harvest")]
    public async Task<IActionResult> GetHarvestReport(
        [FromQuery] HarvestReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result =
            await _harvestReportService.GetHarvestReportAsync(filter,
                cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Báo cáo sản lượng theo ngày, tuần hoặc tháng.
    ///
    /// Ví dụ:
    /// GET /api/reports/harvest/period?period=Day
    /// GET /api/reports/harvest/period?period=Week
    /// GET /api/reports/harvest/period?period=Month
    /// </summary>
    [HttpGet("harvest/period")]
    public async Task<IActionResult> GetHarvestReportByPeriod(
        [FromQuery] HarvestReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result =
            await _harvestReportService
                .GetHarvestReportByPeriodAsync(
                    filter,
                    cancellationToken);

        return Ok(new
        {
            success = true,
            message = (string?)null,
            data = result
        });
    }

    /// <summary>
    /// Báo cáo tồn kho cua đông lạnh.
    /// </summary>
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        CancellationToken cancellationToken)
    {
        var result = await _inventoryReportService
            .GetInventoryReportAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Báo cáo tồn kho với filter thời gian + cảnh báo hết hạn.
    /// </summary>
    [HttpGet("inventory/filtered")]
    public async Task<IActionResult> InventoryFiltered(
        [FromQuery] InventoryReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await _inventoryReportService
            .GetInventoryReportAsync(filter, cancellationToken);
        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// Báo cáo tỷ lệ sống tổng quan.
    /// Có thể lọc theo:
    /// - Thời điểm thả cua
    /// - Khu vực
    /// </summary>
    [HttpGet("survival-rate")]
    public async Task<IActionResult> GetSurvivalRate(
        [FromQuery] SurvivalRateFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await _survivalRateReportService
            .GetSurvivalRateReportAsync(
                filter,
                cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }
    /// <summary>
    /// Báo cáo tỉ lệ lột cua.
    /// </summary>
    [HttpGet("molting")]
    public async Task<IActionResult> GetMoltingReport(
        [FromQuery] MoltingReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await _moltingReportService
            .GetMoltingReportAsync(filter, cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Báo cáo molting với filter thời gian + trend.
    /// </summary>
    [HttpGet("molting/filtered")]
    public async Task<IActionResult> GetMoltingReportFiltered(
        [FromQuery] MoltingReportFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await _moltingReportService
            .GetMoltingReportAsync(filter, cancellationToken);
        return Ok(new { success = true, data = result });
    }

    /// <summary>
    /// Thống kê hiệu quả vận hành.
    /// </summary>
    [HttpGet("operational-efficiency")]
    public async Task<IActionResult> GetOperationalEfficiency(
    [FromQuery] OperationalEfficiencyFilterDto filter,
    CancellationToken cancellationToken)
    {
        var result = await _operationalEfficiencyReportService
            .GetOperationalEfficiencyReportAsync(filter, cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    private sealed record CrabLotCompareRow(
        Guid CrabLotId,
        string LotCode,
        string Name,
        DateTime StartedAt,
        DateTime? EndedAt,
        int DeclaredQuantity,
        int CrabCount,
        int AliveCount,
        int UnmoltedCount,
        int HarvestedCount,
        decimal HarvestedWeightKg);
}