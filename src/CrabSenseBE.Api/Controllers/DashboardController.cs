using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
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

    /// <summary>[READ] Farm overview totals for Home hero — optional farmingAreaId scopes to one khu</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] Guid? farmingAreaId = null, CancellationToken ct = default)
        => Ok(await _dashboard.GetOverviewAsync(farmingAreaId, ct));

    /// <summary>[READ] Farm health score metrics — optional farmingAreaId</summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics([FromQuery] Guid? farmingAreaId = null, CancellationToken ct = default)
        => Ok(await _dashboard.GetMetricsAsync(farmingAreaId, ct));
}

[ApiController]
[Route("api/operations")]
[Authorize]
[Tags("34. Operations")]
[Produces("application/json")]
public class OperationsController : ControllerBase
{
    private readonly IOperationLogService _ops;
    private readonly IFarmOperationService _farmOps;
    private readonly IScheduledTaskService _scheduledTasks;

    public OperationsController(
        IOperationLogService ops,
        IFarmOperationService farmOps,
        IScheduledTaskService scheduledTasks)
    {
        _ops = ops;
        _farmOps = farmOps;
        _scheduledTasks = scheduledTasks;
    }

    /// <summary>[READ] Today's operational tasks for Home</summary>
    [HttpGet("today")]
    public async Task<IActionResult> Today([FromQuery] Guid? farmingAreaId = null, CancellationToken ct = default)
        => Ok(await _ops.GetTodayTasksAsync(farmingAreaId, ct));

    /// <summary>[READ] Recent activity feed</summary>
    [HttpGet("recent")]
    public async Task<IActionResult> Recent(
        [FromQuery] int limit = 20,
        [FromQuery] Guid? farmingAreaId = null,
        CancellationToken ct = default)
        => Ok(await _ops.GetRecentAsync(limit, farmingAreaId, ct));

    /// <summary>[READ] Feeding history grouped by day and crab appetite.</summary>
    [HttpGet("feeding-history")]
    public async Task<IActionResult> FeedingHistory(
        [FromQuery] int days = 7, CancellationToken ct = default)
        => Ok(await _farmOps.GetFeedingHistoryAsync(days, ct));

    /// <summary>[READ] All farm operations, paginated (operation history)</summary>
    [HttpGet]
    public async Task<IActionResult> ListAll(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
        => Ok(await _farmOps.ListAllAsync(page, limit, type, startDate, endDate, ct));

    /// <summary>[READ] Farm operations for a box (Mobile notes/tasks)</summary>
    [HttpGet("box/{boxId:guid}")]
    public async Task<IActionResult> ByBox(
        Guid boxId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
        => Ok(await _farmOps.ListByBoxAsync(boxId, page, limit, type, startDate, endDate, ct));

    /// <summary>[READ] Phiếu chăm sóc của một con cua — lịch sử ăn (Mobile)</summary>
    [HttpGet("crab/{crabId:guid}")]
    public async Task<IActionResult> ByCrab(
        Guid crabId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
        => Ok(await _farmOps.ListByCrabAsync(crabId, page, limit, type, startDate, endDate, ct));

    /// <summary>[CREATE] Create farm operation / note / task</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create(
        [FromBody] CreateFarmOperationRequest req, CancellationToken ct = default)
        => Ok(await _farmOps.CreateAsync(req, ct));

    /// <summary>[CREATE] Alias used by some Mobile clients</summary>
    [HttpPost("create")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateAlias(
        [FromBody] CreateFarmOperationRequest req, CancellationToken ct = default)
        => Ok(await _farmOps.CreateAsync(req, ct));

    /// <summary>[UPDATE] Update farm operation (within 24h)</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateFarmOperationRequest req, CancellationToken ct = default)
        => Ok(await _farmOps.UpdateAsync(id, req, ct));

    /// <summary>[CREATE] Upload operation photo (returns media URL)</summary>
    [HttpPost("photo")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadPhoto(
        IFormFile file,
        [FromForm] Guid? boxId = null,
        [FromForm] Guid? operationId = null,
        [FromForm] string? relatedEntityType = null,
        [FromForm] Guid? relatedEntityId = null,
        [FromServices] IMediaService media = null!,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("file is required."));

        var userIdClaim = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        _ = Guid.TryParse(userIdClaim, out var userId);

        await using var stream = file.OpenReadStream();
        var entityType = string.IsNullOrWhiteSpace(relatedEntityType) ? "FarmOperation" : relatedEntityType;
        var entityId = relatedEntityId ?? operationId;
        var meta = new CrabSenseBE.Application.DTOs.Media.MediaUploadMeta(
            Category: "image",
            BoxId: boxId,
            CrabId: null,
            DeviceId: null,
            RelatedEntityType: entityType,
            RelatedEntityId: entityId,
            Notes: "operation-photo",
            SharePublic: true);

        var uploaded = await media.UploadAsync(stream, file.FileName, file.ContentType, meta, userId, ct);
        var url = uploaded.Data?.ShareLink ?? uploaded.Data?.WebViewLink ?? uploaded.Data?.StorageKey;
        return Ok(ApiResponse<object>.Ok(new { url, mediaId = uploaded.Data?.Id }, "Uploaded."));
    }

    [HttpGet("scheduled-tasks")]
    public async Task<IActionResult> ScheduledTasks(
        [FromQuery] Guid? farmingAreaId = null, CancellationToken ct = default)
        => Ok(await _scheduledTasks.ListAsync(OwnerId(), farmingAreaId, ct));

    [HttpGet("scheduled-tasks/today")]
    public async Task<IActionResult> ScheduledTasksToday(
        [FromQuery] Guid? farmingAreaId = null, CancellationToken ct = default)
        => Ok(await _scheduledTasks.TodayAsync(OwnerId(), farmingAreaId, ct));

    [HttpPost("scheduled-tasks")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateScheduledTask(
        [FromBody] CreateScheduledTaskRequest req, CancellationToken ct = default)
        => Ok(await _scheduledTasks.CreateAsync(OwnerId(), req, ct));

    [HttpPut("scheduled-tasks/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateScheduledTask(
        Guid id, [FromBody] UpdateScheduledTaskRequest req, CancellationToken ct = default)
        => Ok(await _scheduledTasks.UpdateAsync(OwnerId(), id, req, ct));

    [HttpDelete("scheduled-tasks/{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> DeleteScheduledTask(Guid id, CancellationToken ct = default)
        => Ok(await _scheduledTasks.DeleteAsync(OwnerId(), id, ct));

    [HttpPost("scheduled-tasks/{id:guid}/toggle")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> ToggleScheduledTask(
        Guid id, [FromQuery] bool enabled = true, CancellationToken ct = default)
        => Ok(await _scheduledTasks.ToggleAsync(OwnerId(), id, enabled, ct));

    private Guid OwnerId()
    {
        var value = User.FindFirst("sub")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var id) ? id : throw AppException.Unauthorized();
    }
}
