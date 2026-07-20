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

    public ReportsController(
        IHarvestReportService harvestReportService)
    {
        _harvestReportService = harvestReportService;
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
}