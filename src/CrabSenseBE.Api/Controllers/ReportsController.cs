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

    public ReportsController(
        IHarvestReportService harvestReportService,
        IInventoryReportService inventoryReportService)
    {
        _harvestReportService = harvestReportService;
        _inventoryReportService = inventoryReportService;
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
}