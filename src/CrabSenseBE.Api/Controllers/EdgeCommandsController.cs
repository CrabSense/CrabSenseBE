using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
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
            return Ok(await _service.AcknowledgeAsync(
                id,
                request,
                identity!.DeviceCode,
                ct,
                requireDeliveryAttemptId:
                    !identity.IsLegacyCredential
                    && _configuration.GetValue(
                        "EdgeSync:RequireDeliveryAttemptId",
                        false)));
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
