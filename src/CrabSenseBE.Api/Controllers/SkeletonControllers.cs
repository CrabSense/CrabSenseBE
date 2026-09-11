using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Api.Controllers;


[ApiController]
[Route("api/price-lists")]
[Authorize]
[Tags("23. CRUD — Price Lists (stub)")]
[Produces("application/json")]
public class PriceListsController : ControllerBase
{
    /// <summary>[READ] List price lists (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/deliveries")]
[Authorize]
[Tags("25. CRUD — Deliveries (stub)")]
[Produces("application/json")]
public class DeliveriesController : ControllerBase
{
    /// <summary>[READ] List deliveries (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/payments")]
[Authorize]
[Tags("26. CRUD — Payments (stub)")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    /// <summary>[READ] List payments (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}


[ApiController]
[Route("api/settings")]
[Authorize]
[Tags("32. CRUD — Settings (stub)")]
[Produces("application/json")]
public class SettingsController : ControllerBase
{
    /// <summary>[READ] List settings (TODO)</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(new { success = true, data = Array.Empty<object>() });
}

[ApiController]
[Route("api/ai")]
[Authorize]
[Tags("33. AI")]
[Produces("application/json")]
public class AiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IAiOpsService _ai;

    public AiController(IDashboardService dashboard, IAiOpsService ai)
    {
        _dashboard = dashboard;
        _ai = ai;
    }

    /// <summary>[READ] List AI detections — optional boxId / mediaId (videoId alias)</summary>
    [HttpGet("detections")]
    public async Task<IActionResult> Detections(
        [FromQuery] Guid? boxId = null,
        [FromQuery] Guid? mediaId = null,
        [FromQuery] Guid? videoId = null,
        CancellationToken ct = default)
        => Ok(await _ai.ListDetectionsAsync(boxId, mediaId ?? videoId, ct));

    /// <summary>[CREATE] Trigger AI analysis on media/video</summary>
    [HttpPost("analyze")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Analyze(
        [FromBody] AiAnalyzeRequest req, CancellationToken ct = default)
        => Ok(await _ai.AnalyzeAsync(req, ct));

    /// <summary>[CREATE] Submit AI feedback</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(
        [FromBody] AiFeedbackRequest req, CancellationToken ct = default)
    {
        var userIdClaim = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        _ = Guid.TryParse(userIdClaim, out var userId);
        return Ok(await _ai.SubmitFeedbackAsync(req, userId, ct));
    }

    /// <summary>[READ] AI recommendations for Home — optional farmingAreaId</summary>
    [HttpGet("recommendations")]
    public async Task<IActionResult> Recommendations(
        [FromQuery] Guid? farmingAreaId = null,
        CancellationToken ct = default)
        => Ok(await _dashboard.GetRecommendationsAsync(farmingAreaId, ct));
}
