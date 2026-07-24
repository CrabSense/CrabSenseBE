using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>Box QR — scan / update / move crab</summary>
[ApiController]
[Authorize]
[Tags("07. Box QR (scan)")]
[Produces("application/json")]
public class BoxQrController : ControllerBase
{
    private readonly IBoxQrService _service;
    public BoxQrController(IBoxQrService service) => _service = service;

    /// <summary>[CREATE] Ensure / create QR label for box</summary>
    [HttpPost("api/boxes/{boxId:guid}/qr")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> EnsureQr(Guid boxId, CancellationToken ct)
        => Ok(await _service.EnsureBoxQrAsync(boxId, ct));

    /// <summary>[READ] Get QR for box</summary>
    [HttpGet("api/boxes/{boxId:guid}/qr")]
    public async Task<IActionResult> GetQr(Guid boxId, CancellationToken ct)
        => Ok(await _service.GetQrByBoxAsync(boxId, ct));

    /// <summary>[READ] Square PNG image of box QR (for print / sticker)</summary>
    [HttpGet("api/boxes/{boxId:guid}/qr.png")]
    [Produces("image/png")]
    public async Task<IActionResult> GetQrPng(
        Guid boxId, [FromQuery] int size = 8, CancellationToken ct = default)
    {
        var bytes = await _service.GetBoxQrPngAsync(boxId, size, ct);
        return File(bytes, "image/png", $"box-{boxId:N}-qr.png");
    }

    /// <summary>[READ] Scan QR → box + crab info</summary>
    [HttpGet("api/box-qr/scan")]
    [Authorize(Roles = AppRoles.Any)]
    public async Task<IActionResult> Scan([FromQuery] string code, CancellationToken ct)
        => Ok(await _service.ScanAsync(code, ct));

    /// <summary>[READ] Scan QR by path /api/box-qr/{code}</summary>
    [HttpGet("api/box-qr/{code}")]
    [Authorize(Roles = AppRoles.Any)]
    public async Task<IActionResult> ScanByPath(string code, CancellationToken ct)
        => Ok(await _service.ScanAsync(code, ct));

    /// <summary>[READ] Mobile Quick Result alias via box-qr path</summary>
    [HttpGet("api/box-qr/{code}/quick-result")]
    [Authorize(Roles = AppRoles.Any)]
    public async Task<IActionResult> QuickResult(
        string code,
        [FromServices] IBoxDetailService detail,
        CancellationToken ct)
        => Ok(await detail.GetQuickResultByQrAsync(code, ct));

    /// <summary>[UPDATE] Update crab at scanned box</summary>
    [HttpPatch("api/box-qr/{code}/crabs/{crabId:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateCrab(
        string code, Guid crabId, [FromBody] UpdateCrabFromScanRequest req, CancellationToken ct)
        => Ok(await _service.UpdateCrabFromScanAsync(code, crabId, req, ct));

    /// <summary>[UPDATE] Move crab to another box</summary>
    [HttpPost("api/box-qr/{code}/move-crab")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> MoveCrab(
        string code, [FromBody] MoveCrabByScanRequest req, CancellationToken ct)
        => Ok(await _service.MoveCrabAsync(code, req, ct));
}
