using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Truy xuất nguồn gốc — QR cho FrozenLot, HarvestVoucher và endpoint công khai.
/// </summary>
[ApiController]
[Tags("21. Traceability — QR & truy xuất nguồn gốc")]
[Produces("application/json")]
public class TraceabilityController : ControllerBase
{
    private readonly ITraceabilityService _service;

    public TraceabilityController(ITraceabilityService service)
    {
        _service = service;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FrozenLot QR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>[CREATE] Tạo / đảm bảo QR code cho lô cấp đông.</summary>
    [HttpPost("api/frozen-lots/{frozenLotId:guid}/qr")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnsureFrozenLotQr(
        Guid frozenLotId, CancellationToken ct)
        => Ok(await _service.EnsureFrozenLotQrAsync(frozenLotId, ct));

    /// <summary>[READ] Lấy QR data của lô cấp đông.</summary>
    [HttpGet("api/frozen-lots/{frozenLotId:guid}/qr")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFrozenLotQr(
        Guid frozenLotId, CancellationToken ct)
        => Ok(await _service.GetFrozenLotQrAsync(frozenLotId, ct));

    /// <summary>[READ] Ảnh PNG QR cho lô cấp đông (dán tem / in).</summary>
    [HttpGet("api/frozen-lots/{frozenLotId:guid}/qr.png")]
    [Authorize]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFrozenLotQrPng(
        Guid frozenLotId,
        [FromQuery] int size = 8,
        CancellationToken ct = default)
    {
        var bytes = await _service.GetFrozenLotQrPngAsync(frozenLotId, size, ct);
        return File(bytes, "image/png", $"frozen-lot-{frozenLotId:N}-qr.png");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HarvestVoucher QR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>[CREATE] Tạo / đảm bảo QR code cho phiếu thu hoạch.</summary>
    [HttpPost("api/harvest-vouchers/{voucherId:guid}/qr")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnsureHarvestVoucherQr(
        Guid voucherId, CancellationToken ct)
        => Ok(await _service.EnsureHarvestVoucherQrAsync(voucherId, ct));

    /// <summary>[READ] Lấy QR data của phiếu thu hoạch.</summary>
    [HttpGet("api/harvest-vouchers/{voucherId:guid}/qr")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHarvestVoucherQr(
        Guid voucherId, CancellationToken ct)
        => Ok(await _service.GetHarvestVoucherQrAsync(voucherId, ct));

    /// <summary>[READ] Ảnh PNG QR cho phiếu thu hoạch (dán tem / in).</summary>
    [HttpGet("api/harvest-vouchers/{voucherId:guid}/qr.png")]
    [Authorize]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHarvestVoucherQrPng(
        Guid voucherId,
        [FromQuery] int size = 8,
        CancellationToken ct = default)
    {
        var bytes = await _service.GetHarvestVoucherQrPngAsync(voucherId, size, ct);
        return File(bytes, "image/png", $"harvest-{voucherId:N}-qr.png");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Traceability (public)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// [READ] Truy xuất nguồn gốc công khai — quét QR code.
    /// </summary>
    /// <param name="code">Giá trị QR code (tem trên hộp / lô cấp đông / phiếu thu hoạch).</param>
    /// <remarks>
    /// Endpoint này KHÔNG yêu cầu xác thực — dành cho người tiêu dùng cuối.
    ///
    /// Hỗ trợ 3 loại QR:
    /// - **box**: Trả về thông tin box + phiếu thu hoạch + lô cấp đông liên quan
    /// - **frozen_lot**: Trả về thông tin lô cấp đông + phiếu thu hoạch
    /// - **harvest_voucher**: Trả về thông tin phiếu thu hoạch + lô cấp đông
    /// </remarks>
    [HttpGet("api/traceability/{code}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraceability(
        string code, CancellationToken ct)
        => Ok(await _service.GetTraceabilityAsync(code, ct));

    /// <summary>
    /// [READ] Truy xuất nguồn gốc — query param.
    /// </summary>
    [HttpGet("api/traceability/scan")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraceabilityByQuery(
        [FromQuery] string code, CancellationToken ct)
        => Ok(await _service.GetTraceabilityAsync(code, ct));
}
