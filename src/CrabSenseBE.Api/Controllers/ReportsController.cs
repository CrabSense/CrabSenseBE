using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "SystemAdmin")]
public class ReportsController : ControllerBase
{
    private readonly IHarvestReportService _harvestReportService;
    private readonly IInventoryReportService _inventoryReportService;

    private readonly ISurvivalRateReportService _survivalRateReportService;

    public ReportsController(
        IHarvestReportService harvestReportService,
        IInventoryReportService inventoryReportService,
        ISurvivalRateReportService survivalRateReportService)
    {
        _harvestReportService = harvestReportService;
        _inventoryReportService = inventoryReportService;
        _survivalRateReportService = survivalRateReportService;
    }


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
    /// </summary>
    [HttpGet("survival-rate")]
    public async Task<IActionResult> GetSurvivalRate(
        CancellationToken cancellationToken)
    {
        var result = await _survivalRateReportService
            .GetSurvivalRateReportAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Báo cáo tỷ lệ sống theo đợt nuôi.
    /// </summary>
    [HttpGet("survival-rate/by-crop-batch")]
    public async Task<IActionResult> GetSurvivalRateByCropBatch(
        [FromQuery] Guid cropBatchId,
        CancellationToken cancellationToken)
    {
        var result = await _survivalRateReportService
            .GetSurvivalRateByCropBatchAsync(cropBatchId, cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }

    /// <summary>
    /// Báo cáo tỷ lệ sống theo khu vực.
    /// </summary>
    [HttpGet("survival-rate/by-area")]
    public async Task<IActionResult> GetSurvivalRateByArea(
        [FromQuery] Guid areaId,
        CancellationToken cancellationToken)
    {
        var result = await _survivalRateReportService
            .GetSurvivalRateByAreaAsync(areaId, cancellationToken);

        return Ok(new
        {
            success = true,
            data = result
        });
    }
}