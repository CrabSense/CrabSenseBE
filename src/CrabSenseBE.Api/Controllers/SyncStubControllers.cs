using CrabSenseBE.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Stub sync endpoints cho FE / Kiosk — trả skeleton, bổ sung logic sau.
/// Giữ tách khỏi <see cref="SyncController"/> (HDF5).
/// </summary>
[ApiController]
[Authorize]
[Tags("09b. Sync stubs (FE)")]
[Produces("application/json")]
public class SyncQueueStubController : ControllerBase
{
    /// <summary>[READ] Hàng đợi sync (stub)</summary>
    [HttpGet("/api/sync/queue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSyncQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
        => Ok(ApiResponse<object>.Ok(new
        {
            items = Array.Empty<object>(),
            totalCount = 0,
            page,
            pageSize,
            status,
            stub = true
        }, "TODO: implement /api/sync/queue"));

    /// <summary>[CREATE] Enqueue sync job (stub)</summary>
    [HttpPost("/api/sync/queue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult EnqueueSync([FromBody] object? body)
        => Ok(ApiResponse<object>.Ok(new
        {
            id = Guid.Empty,
            status = "queued",
            received = body,
            stub = true
        }, "TODO: implement POST /api/sync/queue"));
}

[ApiController]
[Authorize]
[Route("api/v1/sync")]
[Tags("09b. Sync stubs (FE)")]
[Produces("application/json")]
public class SyncV1StubController : ControllerBase
{
    /// <summary>[READ/ACTION] Pull sync payload (stub) — FE có thể GET hoặc POST</summary>
    [HttpGet("pull")]
    [HttpPost("pull")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Pull(
        [FromQuery] DateTime? since = null,
        [FromQuery] string? deviceCode = null,
        [FromBody] object? body = null)
        => Ok(ApiResponse<object>.Ok(new
        {
            since,
            deviceCode,
            cursor = (string?)null,
            changes = Array.Empty<object>(),
            request = body,
            stub = true
        }, "TODO: implement /api/v1/sync/pull"));

    /// <summary>[READ] Danh sách thay đổi từ mốc thời gian (stub)</summary>
    [HttpGet("changes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Changes(
        [FromQuery] DateTime? since = null,
        [FromQuery] DateTime? until = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entity = null)
        => Ok(ApiResponse<object>.Ok(new
        {
            since,
            until,
            entity,
            page,
            pageSize,
            items = Array.Empty<object>(),
            totalCount = 0,
            stub = true
        }, "TODO: implement /api/v1/sync/changes"));

    /// <summary>[READ] Download artifact / file sync (stub)</summary>
    [HttpGet("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Download(
        [FromQuery] Guid? id = null,
        [FromQuery] string? code = null,
        [FromQuery] string? type = null)
        => Ok(ApiResponse<object>.Ok(new
        {
            id,
            code,
            type,
            url = (string?)null,
            contentType = (string?)null,
            sizeBytes = 0,
            stub = true
        }, "TODO: implement /api/v1/sync/download — có thể đổi sang FileResult sau"));
}

[ApiController]
[Authorize]
[Route("api/v1/synchronization")]
[Tags("09b. Sync stubs (FE)")]
[Produces("application/json")]
public class SynchronizationV1StubController : ControllerBase
{
    /// <summary>[READ] Hàng đợi synchronization v1 (stub) — alias FE</summary>
    [HttpGet("queue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
        => Ok(ApiResponse<object>.Ok(new
        {
            items = Array.Empty<object>(),
            totalCount = 0,
            page,
            pageSize,
            status,
            stub = true
        }, "TODO: implement /api/v1/synchronization/queue"));

    /// <summary>[CREATE] Enqueue synchronization job (stub)</summary>
    [HttpPost("queue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Enqueue([FromBody] object? body)
        => Ok(ApiResponse<object>.Ok(new
        {
            id = Guid.Empty,
            status = "queued",
            received = body,
            stub = true
        }, "TODO: implement POST /api/v1/synchronization/queue"));
}
