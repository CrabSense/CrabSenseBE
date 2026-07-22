using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// Truy xuất nguồn gốc — QR cho FrozenLot, HarvestVoucher và endpoint công khai.
/// </summary>
public interface ITraceabilityService
{
    // ── FrozenLot QR ──────────────────────────────────────────────────────

    /// <summary>Tạo / đảm bảo QR code cho lô cấp đông.</summary>
    Task<ApiResponse<QrCodeDto>> EnsureFrozenLotQrAsync(
        Guid frozenLotId,
        CancellationToken ct = default);

    /// <summary>Lấy QR data của lô cấp đông.</summary>
    Task<ApiResponse<QrCodeDto>> GetFrozenLotQrAsync(
        Guid frozenLotId,
        CancellationToken ct = default);

    /// <summary>Ảnh PNG QR cho lô cấp đông.</summary>
    Task<byte[]> GetFrozenLotQrPngAsync(
        Guid frozenLotId,
        int pixelsPerModule = 8,
        CancellationToken ct = default);

    // ── HarvestVoucher QR ─────────────────────────────────────────────────

    /// <summary>Tạo / đảm bảo QR code cho phiếu thu hoạch.</summary>
    Task<ApiResponse<QrCodeDto>> EnsureHarvestVoucherQrAsync(
        Guid voucherId,
        CancellationToken ct = default);

    /// <summary>Lấy QR data của phiếu thu hoạch.</summary>
    Task<ApiResponse<QrCodeDto>> GetHarvestVoucherQrAsync(
        Guid voucherId,
        CancellationToken ct = default);

    /// <summary>Ảnh PNG QR cho phiếu thu hoạch.</summary>
    Task<byte[]> GetHarvestVoucherQrPngAsync(
        Guid voucherId,
        int pixelsPerModule = 8,
        CancellationToken ct = default);

    // ── Traceability (public) ─────────────────────────────────────────────

    /// <summary>
    /// Decode QR code → trả về thông tin truy xuất nguồn gốc công khai.
    /// Hỗ trợ cả 3 loại: box, frozen_lot, harvest_voucher.
    /// </summary>
    Task<ApiResponse<TraceabilityPublicDto>> GetTraceabilityAsync(
        string code,
        CancellationToken ct = default);
}
