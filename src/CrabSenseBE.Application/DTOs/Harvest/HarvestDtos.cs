namespace CrabSenseBE.Application.DTOs.Harvest;

// ─────────────────────────────────────────────────────────────────────────────
// Harvest Voucher
// ─────────────────────────────────────────────────────────────────────────────

public record HarvestVoucherDto(
    Guid Id,
    string VoucherCode,
    Guid? CropBatchId,
    DateTime HarvestDate,
    string Status,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    string? Notes,
    Guid CreatedBy,
    DateTime CreatedAt
);

public record HarvestVoucherDetailDto(
    Guid Id,
    string VoucherCode,
    Guid? CropBatchId,
    DateTime HarvestDate,
    string Status,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    string? Notes,
    Guid CreatedBy,
    DateTime CreatedAt,
    IReadOnlyCollection<HarvestLineDto> Lines
);

public record HarvestLineDto(
    Guid Id,
    Guid? CrabId,
    decimal WeightGram,
    string? Grade,
    bool IsSoftshell,
    string? Notes
);

public record CreateHarvestVoucherRequest(
    Guid? CropBatchId,
    DateTime HarvestDate,
    string? Notes,
    IEnumerable<HarvestLineRequest> Lines
);

public record HarvestLineRequest(
    Guid? CrabId,
    decimal WeightGram,
    string? Grade,
    bool IsSoftshell,
    string? Notes = null
);

public record UpdateHarvestStatusRequest(
    string Status
);

// ─────────────────────────────────────────────────────────────────────────────
// Harvest Statistics
// ─────────────────────────────────────────────────────────────────────────────

public record HarvestStatisticsDto(
    DateTime From,
    DateTime To,
    string Period,
    int VoucherCount,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    IReadOnlyCollection<HarvestPeriodItemDto> Items
);

public record HarvestPeriodItemDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int VoucherCount,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate
);

// ─────────────────────────────────────────────────────────────────────────────
// Frozen Inventory
// ─────────────────────────────────────────────────────────────────────────────

public record FrozenLotDto(
    Guid Id,
    string LotCode,
    Guid? HarvestVoucherId,
    DateTime FrozenDate,
    DateTime ExpiryDate,
    decimal WeightKg,
    string? Grade,
    int Quantity,
    string Status,
    string? StorageLocation,
    int StorageDays,
    int RemainingShelfLifeDays,
    bool IsNearExpiry,
    bool IsExpired,
    DateTime CreatedAt
);

public record CreateFrozenLotRequest(
    Guid? HarvestVoucherId,
    DateTime FrozenDate,
    DateTime ExpiryDate,
    decimal WeightKg,
    string? Grade,
    int Quantity,
    string? StorageLocation
);

public record UpdateFrozenLotRequest(
    DateTime? ExpiryDate,
    string? Grade,
    string? StorageLocation
);

public record UpdateFrozenLotStatusRequest(
    string Status
);

public record FrozenInventorySummaryDto(
    int TotalLots,
    int AvailableLots,
    int ReservedLots,
    int ShippedLots,
    int NearExpiryLots,
    int ExpiredLots,
    int TotalQuantity,
    decimal TotalWeightKg
);

// ─────────────────────────────────────────────────────────────────────────────
// QR / Traceability
// ─────────────────────────────────────────────────────────────────────────────

public record QrCodeDto(
    Guid Id,
    string Code,
    Guid? FrozenLotId,
    int ScanCount,
    DateTime? ExpiresAt
);

public record GenerateQrRequest(
    Guid? FrozenLotId,
    Guid? HarvestVoucherId,
    DateTime? ExpiresAt
);

public record TraceabilityPublicDto(
    string Code,
    string? FrozenLotCode,
    string? HarvestVoucherCode,
    DateTime? FrozenDate,
    DateTime? HarvestDate,
    string? Grade
);
