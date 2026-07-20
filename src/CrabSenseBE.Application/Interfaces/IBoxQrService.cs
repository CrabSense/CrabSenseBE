using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;

namespace CrabSenseBE.Application.Interfaces;

/// <summary>
/// QR trên từng hộp nuôi:
/// - Tạo/đảm bảo mỗi box có 1 mã QR
/// - Quét → xem hộp + cua hiện tại
/// - Cập nhật cua / di dời giữa các hộp tại hiện trường
/// </summary>
public interface IBoxQrService
{
    Task<ApiResponse<BoxQrDto>> EnsureBoxQrAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<BoxQrDto>> GetQrByBoxAsync(Guid boxId, CancellationToken ct = default);

    /// <summary>Square PNG (QR code) for box sticker print / preview.</summary>
    Task<byte[]> GetBoxQrPngAsync(Guid boxId, int pixelsPerModule = 8, CancellationToken ct = default);

    /// <summary>Quét mã (Code trên tem) → hồ sơ hộp + cua đang nuôi.</summary>
    Task<ApiResponse<BoxScanResultDto>> ScanAsync(string code, CancellationToken ct = default);

    Task<ApiResponse<CrabDto>> UpdateCrabFromScanAsync(
        string boxQrCode, Guid crabId, UpdateCrabFromScanRequest req, CancellationToken ct = default);

    /// <summary>Chuyển cua sang hộp khác (TargetBoxId hoặc TargetQrCode).</summary>
    Task<ApiResponse<CrabBoxAllocationDto>> MoveCrabAsync(
        string sourceBoxQrCode, MoveCrabByScanRequest req, CancellationToken ct = default);
}
