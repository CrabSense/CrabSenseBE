namespace CrabSenseBE.Application.DTOs.Farm;

/// <summary>Tem QR gắn trên hộp — App encode Code hoặc Payload URL.</summary>
public record BoxQrDto(
    Guid Id,
    string Code,
    Guid BoxId,
    string? Payload,
    int ScanCount,
    bool IsActive);

/// <summary>
/// Kết quả quét QR hộp: thông tin box + cua đang ở trong + lịch sử gần đây.
/// Dùng màn hình mobile khi NV cầm điện thoại quét tem trên hộp.
/// </summary>
public record BoxScanResultDto(
    BoxQrDto Qr,
    BoxScanBoxDto Box,
    IReadOnlyList<BoxScanCrabDto> Crabs,
    string AreaName,
    string RowName);

public record BoxScanBoxDto(
    Guid Id,
    string Code,
    string? Status,
    bool IsOccupied,
    Guid FarmingRowId);

public record BoxScanCrabDto(
    Guid Id,
    string? Tag,
    decimal? WeightGram,
    string? MoltingStage,
    bool IsAlive,
    DateTime? MoltedAt,
    Guid CrabLotId,
    DateTime? CurrentAllocationStart);

/// <summary>NV cập nhật nhanh thông tin cua ngay trên màn quét QR.</summary>
public record UpdateCrabFromScanRequest(
    string? Tag,
    decimal? WeightGram,
    string? MoltingStage,
    bool? IsAlive,
    DateTime? MoltedAt);

/// <summary>Di dời cua từ hộp đang quét sang hộp đích (theo BoxId hoặc mã QR đích).</summary>
public record MoveCrabByScanRequest(
    Guid CrabId,
    Guid? TargetBoxId,
    string? TargetQrCode,
    string? Notes);

/// <summary>
/// Mobile Scan QR Quick Result — one payload after a successful scan
/// (box snapshot + health/AI scores + water + alerts + recommendation).
/// </summary>
public record BoxQrQuickResultDto(
    Guid BoxId,
    string Code,
    string RawValue,
    Guid FarmId,
    string FarmName,
    string RowName,
    string StatusLabel,
    string HealthStatus,
    int HealthScore,
    int AiScore,
    int CrabCount,
    decimal? Temperature,
    decimal? Ph,
    DateTime? UpdatedAt,
    string? AiRecommendation,
    double? AiConfidence,
    IReadOnlyList<BoxQrQuickAlertDto> Alerts);

public record BoxQrQuickAlertDto(
    string Title,
    string Severity);
