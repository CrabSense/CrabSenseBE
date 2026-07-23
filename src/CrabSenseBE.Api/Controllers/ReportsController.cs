using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "SystemAdmin")]
public class ReportsController : ControllerBase
{
    private readonly IHarvestReportService _harvestReportService;
    private readonly IInventoryReportService _inventoryReportService;
    private readonly ISurvivalRateReportService _survivalRateReportService;
    private readonly IMoltingReportService _moltingReportService;
    private readonly IOperationalEfficiencyReportService _operationalEfficiencyReportService;

    public ReportsController(
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
    }

    /// <summary>
    /// Báo cáo tỉ lệ thu hoạch.
    /// </summary>
    [HttpGet("harvest")]
    public async Task<IActionResult> GetHarvestReport(
        CancellationToken cancellationToken)
    {
        var result =
            await _harvestReportService.GetHarvestReportAsync(
                cancellationToken);

        return Ok(result);
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
        CancellationToken cancellationToken)
    {
        var result = await _moltingReportService
            .GetMoltingReportAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
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
}