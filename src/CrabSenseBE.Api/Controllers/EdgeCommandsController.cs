using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CrabSenseBE.Application.Services;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/edge/commands")]
[Tags("09. Edge command queue")]
public sealed class EdgeCommandsController : ControllerBase
{
    private readonly IEdgeCommandService _service;
    private readonly IConfiguration _configuration;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public EdgeCommandsController(
        IEdgeCommandService service,
        IConfiguration configuration,
        IUnitOfWork uow,
        ICurrentUserService currentUser)
    {
        _service = service;
        _configuration = configuration;
        _uow = uow;
        _currentUser = currentUser;
    }

    // Read-only platform diagnostics; never grants permission to enqueue a relay command.
    [HttpGet("history/{deviceId:guid}")]
    [Authorize(Roles = AppRoles.Platform)]
    public async Task<IActionResult> History(Guid deviceId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, [FromQuery] string? status = null,
        CancellationToken ct = default, [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null, [FromQuery] string? channel = null)
    {
        var actor = await new FarmAreaScope(_uow, _currentUser).CurrentUserAsync(ct);
        if (actor?.Role != UserRole.SystemAdministrator)
            return Forbid();
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { success = false, message = "Invalid pagination." });
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct);
        if (device is null)
            return NotFound(new { success = false, message = "Controller not found." });
        var query = _uow.EdgeCommands.Query().AsNoTracking().Where(x => x.DeviceCode == device.DeviceCode);
        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim().ToLowerInvariant();
            if (status is not ("pending" or "delivered" or "acknowledged" or "failed" or "timedout" or "superseded"))
                return BadRequest(new { success = false, message = "Invalid command status." });
            query = query.Where(x => x.Status == status);
        }
        if (from.HasValue && to.HasValue && from.Value >= to.Value)
            return BadRequest(new { success = false, message = "Invalid date range." });
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value.UtcDateTime);
        if (to.HasValue) query = query.Where(x => x.CreatedAt < to.Value.UtcDateTime);
        if (!string.IsNullOrWhiteSpace(channel))
        {
            channel = channel.Trim();
            if (channel.Length > 64) return BadRequest(new { success = false, message = "Invalid channel." });
            query = query.Where(x => x.Channel == channel);
        }
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.DeviceCode, x.Command, x.Channel, x.Status, x.CreatedAt,
                x.DeliveredAt, x.AcknowledgedAt, x.FailedAt, x.ExpiresAt, x.AttemptCount,
                x.ResultMessage, x.RequestSource, x.CommandSequence, x.InventoryRevision, x.SupersededAt })
            .ToListAsync(ct);
        return Ok(new { success = true, data = new { items, totalCount, page, pageSize } });
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.FarmManager + "," + AppRoles.FarmStaff + ",Staff")]
    public Task<IActionResult> Enqueue(
        [FromBody] EnqueueEdgeCommandRequest request,
        CancellationToken ct)
        => Execute(async () =>
        {
            if (!_currentUser.IsAuthenticated)
                throw CrabSenseBE.Application.Common.AppException.Unauthorized(
                    "Không xác định được người gửi lệnh.");
            var userId = _currentUser.UserId;
            if (userId == Guid.Empty)
                throw CrabSenseBE.Application.Common.AppException.Unauthorized(
                    "UserId không hợp lệ.");
            return Ok(await _service.EnqueueManualAsync(request, userId, ct));
        });

    [HttpGet("pending")]
    [AllowAnonymous]
    public Task<IActionResult> Pending([FromQuery] string deviceCode, CancellationToken ct)
        => Execute(async () =>
        {
            var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
                Request, _configuration, _uow, deviceCode, ct);
            if (denied is not null) return denied;
            if (Request.Headers["X-Edge-Protocol"].ToString() != "2")
                throw CrabSenseBE.Application.Common.AppException.Conflict(
                    "Firmware cần Edge protocol 2 (command sequence) trước khi nhận lệnh relay.");
            return Ok(await _service.GetPendingAsync(
                identity!.DeviceCode, identity.LeaseHolder, ct));
        });

    [HttpPost("{id:guid}/ack")]
    [AllowAnonymous]
    public Task<IActionResult> Acknowledge(
        Guid id,
        [FromBody] EdgeCommandAckRequest request,
        CancellationToken ct)
        => Execute(async () =>
        {
            var command = await _uow.EdgeCommands.GetByIdAsync(id, ct);
            if (command is null)
                throw CrabSenseBE.Application.Common.AppException.NotFound("EdgeCommand");
            var (denied, identity) = await EdgeKeyGuard.CheckDeviceAsync(
                Request, _configuration, _uow, command.DeviceCode, ct);
            if (denied is not null) return denied;
            if (Request.Headers["X-Edge-Protocol"].ToString() != "2")
                throw CrabSenseBE.Application.Common.AppException.Conflict("Edge protocol 2 là bắt buộc.");
            return Ok(await _service.AcknowledgeAsync(
                id,
                request,
                identity!.DeviceCode,
                ct,
                requireDeliveryAttemptId: true));
        });

    private static async Task<IActionResult> Execute(
        Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CrabSenseBE.Application.Common.AppException ex)
        {
            return new ObjectResult(new { success = false, message = ex.Message })
            {
                StatusCode = ex.StatusCode
            };
        }
    }
}
