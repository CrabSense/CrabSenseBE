using CrabSenseBE.Application.DTOs.Kiosk;
using CrabSenseBE.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/kiosks")]
[Authorize]
[Tags("09. Kiosk provisioning")]
public sealed class KiosksController : ControllerBase
{
    private readonly IKioskProvisioningService _kiosks;

    public KiosksController(IKioskProvisioningService kiosks) => _kiosks = kiosks;

    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateKioskRequest request, CancellationToken ct)
        => Execute(() => _kiosks.CreateAsync(request, ct));

    [HttpGet]
    public Task<IActionResult> List([FromQuery] Guid farmingAreaId, CancellationToken ct)
        => Execute(() => _kiosks.ListAsync(farmingAreaId, ct));

    [HttpPost("{id:guid}/provisioning-codes")]
    public Task<IActionResult> IssueCode(Guid id, CancellationToken ct)
        => Execute(() => _kiosks.IssueCodeAsync(id, ct));

    [HttpPost("{id:guid}/revoke")]
    public Task<IActionResult> Revoke(Guid id, CancellationToken ct)
        => Execute(() => _kiosks.RevokeAsync(id, ct));

    [HttpDelete("{id:guid}")]
    public Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Execute(() => _kiosks.DeleteAsync(id, ct));

    [HttpPost("reset")]
    public Task<IActionResult> Reset([FromQuery] Guid farmingAreaId, CancellationToken ct)
        => Execute(() => _kiosks.ResetAreaAsync(farmingAreaId, ct));

    [HttpGet("controllers")]
    public Task<IActionResult> Controllers([FromQuery] Guid farmingAreaId, CancellationToken ct)
        => Execute(() => _kiosks.ListControllersAsync(farmingAreaId, ct));

    [HttpPost("/api/controllers/{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id, CancellationToken ct)
        => Execute(() => _kiosks.ApproveControllerAsync(id, ct));

    [HttpPost("/api/controllers/{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id, CancellationToken ct)
        => Execute(() => _kiosks.RejectControllerAsync(id, ct));

    [AllowAnonymous]
    [HttpPost("/api/controllers/register")]
    public Task<IActionResult> Register([FromBody] RegisterControllerRequest request, CancellationToken ct)
        => Execute(() => _kiosks.RegisterControllerAsync(EdgeKey(), request, ct));

    [AllowAnonymous]
    [HttpPost("heartbeat")]
    public Task<IActionResult> Heartbeat([FromBody] KioskHeartbeatBody? body, CancellationToken ct)
        => Execute(() => _kiosks.TouchAsync(EdgeKey(), body?.LanIp, ct));

    [AllowAnonymous]
    [HttpGet("me")]
    public Task<IActionResult> Me(CancellationToken ct)
        => Execute(() => _kiosks.TouchAsync(EdgeKey(), null, ct));

    private string EdgeKey() => Request.Headers["X-Kiosk-Key"].FirstOrDefault() ?? "";

    private async Task<IActionResult> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (CrabSenseBE.Application.Common.AppException ex)
        {
            return new ObjectResult(new { success = false, message = ex.Message })
            {
                StatusCode = ex.StatusCode
            };
        }
    }

    public sealed record KioskHeartbeatBody(string? LanIp);
}

[ApiController]
[Route("api/provision")]
[Tags("09. Kiosk provisioning")]
public sealed class KioskProvisionController : ControllerBase
{
    private readonly IKioskProvisioningService _kiosks;

    public KioskProvisionController(IKioskProvisioningService kiosks) => _kiosks = kiosks;

    [AllowAnonymous]
    [HttpPost("kiosk")]
    public async Task<IActionResult> Redeem([FromBody] RedeemKioskRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _kiosks.RedeemAsync(request, ct));
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
