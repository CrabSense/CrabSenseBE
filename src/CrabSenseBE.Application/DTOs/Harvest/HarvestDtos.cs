namespace CrabSenseBE.Application.DTOs.Harvest;

// ─────────────────────────────────────────────────────────────────────────────
// Harvest Voucher
// ─────────────────────────────────────────────────────────────────────────────

public record HarvestVoucherDto(
    Guid Id,
    string VoucherCode,
    // Guid? CropBatchId,
    DateTime HarvestDate,
    string Status,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    string? Notes,
    Guid CreatedBy,
    DateTime CreatedAt,
    Guid? FarmingAreaId = null,
    string? PerformedByName = null,
    string? AreaName = null,
    int PassedCount = 0,
    int FailedCount = 0,
    decimal AverageWeightGram = 0,
    IReadOnlyList<string>? PhotoUrls = null,
    IReadOnlyCollection<HarvestLineDto>? Lines = null
);

public record HarvestVoucherDetailDto(
    Guid Id,
    string VoucherCode,
    // Guid? CropBatchId,
    DateTime HarvestDate,
    string Status,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    string? Notes,
    Guid CreatedBy,
    DateTime CreatedAt,
    IReadOnlyCollection<HarvestLineDto> Lines,
    Guid? FarmingAreaId = null,
    string? PerformedByName = null,
    string? AreaName = null,
    int PassedCount = 0,
    int FailedCount = 0,
    decimal AverageWeightGram = 0,
    IReadOnlyList<string>? PhotoUrls = null
);

public record HarvestLineDto(
    Guid Id,
    Guid? CrabId,
    Guid? BoxId,            // ← THÊM MỚI
    string? BoxCode,
    decimal WeightGram,
    string? Grade,
    bool IsSoftshell,
    string? Notes,
    string? CrabCode = null,
    string? BoxCode = null,
    string? ConditionLabel = null,
    IReadOnlyList<string>? PhotoUrls = null,
    string? AreaName = null,
    string? RowName = null,
    string? LotCode = null,
    string? Result = null
);

public record CreateHarvestVoucherRequest(
    Guid? CropBatchId,
    DateTime HarvestDate,
    string? Notes,
    IEnumerable<HarvestLineRequest> Lines,
    Guid? FarmingAreaId = null,
    string? PerformedByName = null,
    IEnumerable<string>? PhotoUrls = null
);

public record HarvestLineRequest(
    Guid? CrabId,
    decimal WeightGram,
    string? Grade,
    bool IsSoftshell,
    string? Notes = null,
    string? ConditionLabel = null,
    IEnumerable<string>? PhotoUrls = null,
    string? Result = null
);

public record HarvestOverviewDto(
    int HarvestableCount,
    int HarvestedToday,
    int WaitingSale,
    decimal TotalHarvestWeightKg
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

// ─────────────────────────────────────────────────────────────────────────────
// Box ↔ Harvest link
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Thông tin box tham chiếu trong phiếu thu hoạch.</summary>
public record HarvestBoxDto(
    Guid BoxId,
    string BoxCode,
    int CrabCount,
    decimal TotalWeightGram
);

/// <summary>Thông tin phiếu thu hoạch tham chiếu đến box.</summary>
public record BoxHarvestVoucherDto(
    Guid VoucherId,
    string VoucherCode,
    DateTime HarvestDate,
    string Status,
    int CrabCount,
    decimal TotalWeightGram
);
