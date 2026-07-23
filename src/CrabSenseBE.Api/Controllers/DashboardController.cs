using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
[Tags("31. Dashboard")]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    /// <summary>[READ] Farm overview totals for Home hero</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
        => Ok(await _dashboard.GetOverviewAsync(ct));

    /// <summary>[READ] Farm health score metrics</summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(CancellationToken ct)
        => Ok(await _dashboard.GetMetricsAsync(ct));
}

[ApiController]
[Route("api/operations")]
[Authorize]
[Tags("34. Operations")]
[Produces("application/json")]
public class OperationsController : ControllerBase
{
    private readonly IOperationLogService _ops;

    public OperationsController(IOperationLogService ops) => _ops = ops;

    /// <summary>[READ] Today's operational tasks for Home</summary>
    [HttpGet("today")]
    public async Task<IActionResult> Today(CancellationToken ct)
        => Ok(await _ops.GetTodayTasksAsync(ct));

    /// <summary>[READ] Recent activity feed</summary>
    [HttpGet("recent")]
    public async Task<IActionResult> Recent([FromQuery] int limit = 20, CancellationToken ct = default)
        => Ok(await _ops.GetRecentAsync(limit, ct));
}
