namespace CrabSenseBE.Application.DTOs.FrozenStorage;

/// <summary>Thông tin lô cua cấp đông.</summary>
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
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid? FarmingAreaId = null
);

/// <summary>Yêu cầu tạo lô cua cấp đông.</summary>
public record CreateFrozenLotRequest(
    string LotCode,
    Guid? HarvestVoucherId,
    DateTime FrozenDate,
    DateTime ExpiryDate,
    decimal WeightKg,
    string? Grade,
    int Quantity,
    string? StorageLocation,
    Guid? FarmingAreaId = null
);

/// <summary>Yêu cầu cập nhật lô cua cấp đông.</summary>
public record UpdateFrozenLotRequest(
    DateTime FrozenDate,
    DateTime ExpiryDate,
    decimal WeightKg,
    string? Grade,
    int Quantity,
    string Status,
    string? StorageLocation
);

/// <summary>Tổng hợp tồn kho cấp đông.</summary>
public record FrozenInventorySummaryDto(
    int TotalLots,
    int TotalQuantity,
    decimal TotalWeightKg,
    int AvailableLots,
    int AvailableQuantity,
    decimal AvailableWeightKg,
    int ReservedLots,
    int ReservedQuantity,
    decimal ReservedWeightKg,
    int ExpiredLots,
    int ExpiredQuantity,
    decimal ExpiredWeightKg,
    IEnumerable<FrozenInventoryByGradeDto> ByGrade,
    IEnumerable<FrozenInventoryByLocationDto> ByLocation,
    IEnumerable<FrozenInventoryByStatusDto> ByStatus
);

/// <summary>Tồn kho được phân nhóm theo kích cỡ.</summary>
public record FrozenInventoryByGradeDto(
    string Grade,
    int TotalLots,
    int TotalQuantity,
    decimal TotalWeightKg
);

/// <summary>Tồn kho được phân nhóm theo vị trí lưu trữ.</summary>
public record FrozenInventoryByLocationDto(
    string StorageLocation,
    int TotalLots,
    int TotalQuantity,
    decimal TotalWeightKg
);

/// <summary>Tồn kho được phân nhóm theo trạng thái.</summary>
public record FrozenInventoryByStatusDto(
    string Status,
    int TotalLots,
    int TotalQuantity,
    decimal TotalWeightKg
);

public record FrozenStorageAgingDto(
    Guid Id,
    string LotCode,
    DateTime FrozenDate,
    DateTime ExpiryDate,
    int StorageDays,
    int RemainingDays,
    string AgingStatus,
    string? Grade,
    int Quantity,
    decimal WeightKg,
    string? StorageLocation
);

/// <summary>
/// Thông tin lô cấp đông sắp hết thời gian bảo quản.
/// </summary>
public record ExpiringFrozenLotDto(
    Guid Id,
    string LotCode,
    DateTime FrozenDate,
    DateTime ExpiryDate,
    int StorageDays,
    int RemainingDays,
    string AlertLevel,
    string Status,
    string? Grade,
    int Quantity,
    decimal WeightKg,
    string? StorageLocation
);
/// <summary>
/// Thông tin của một cua được đóng gói trong lô.
/// WeightGram và Grade là số liệu riêng của từng cua.
/// </summary>
public record FrozenCrabItemInput(
    Guid HarvestLineId,
    decimal WeightGram,
    string Grade);

/// <summary>
/// Đăng ký toàn bộ sản phẩm cua trong lô để sinh barcode.
/// </summary>
public record RegisterFrozenCrabItemsRequest(
    IReadOnlyCollection<FrozenCrabItemInput> Items);

/// <summary>
/// Dữ liệu trả về khi tạo hoặc tra cứu barcode cua cấp đông.
/// </summary>
public record FrozenCrabItemDto(
    Guid Id,
    string BarcodeValue,
    string LotCode,
    string CrabCode,
    string? HarvestVoucherCode,
    DateTime HarvestDate,
    DateTime FrozenDate,
    DateTime ExpiryDate,
    decimal WeightGram,
    string Grade,
    string StockState = "Unverified",
    DateTime? DispatchedAt = null,
    Guid? OutboundTransactionId = null);

public record ReconcileFrozenItemsRequest(IReadOnlyList<Guid> AvailableItemIds, string Reason);

public record CreateFrozenStorageTransactionRequest(
    Guid FrozenLotId,
    string Type,
    int RequestedQuantity,
    decimal RequestedWeightKg,
    string? Notes = null);

public record RejectFrozenStorageTransactionRequest(string Reason);

public record ExecuteFrozenStorageTransactionRequest(
    int ActualQuantity,
    decimal ActualWeightKg,
    string? Notes = null,
    string? VarianceReason = null,
    IReadOnlyList<Guid>? FrozenCrabItemIds = null);

public record FrozenStorageTransactionDto(
    Guid Id,
    string TransactionCode,
    Guid FrozenLotId,
    Guid FarmingAreaId,
    string Type,
    string Status,
    int RequestedQuantity,
    decimal RequestedWeightKg,
    int? ActualQuantity,
    decimal? ActualWeightKg,
    Guid CreatedByUserId,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAt,
    string? RejectionReason,
    Guid? PerformedByUserId,
    DateTime? PerformedAt,
    string? Notes,
    DateTime CreatedAt,
    int? ApprovedQuantity = null,
    decimal? ApprovedWeightKg = null,
    int? QuantityVariance = null,
    decimal? WeightVarianceKg = null,
    string? VarianceReason = null);

public record FrozenInventoryAdjustmentDto(
    Guid Id,
    Guid FrozenLotId,
    Guid FrozenStorageTransactionId,
    Guid FarmingAreaId,
    int QuantityDelta,
    decimal WeightDeltaKg,
    int QuantityBefore,
    int QuantityAfter,
    decimal WeightBeforeKg,
    decimal WeightAfterKg,
    Guid PerformedByUserId,
    string? Reason,
    DateTime CreatedAt);

public record VerifyFrozenOpeningBalanceRequest(
    int OpeningQuantity,
    decimal OpeningWeightKg,
    string Reason);

public record AssignFrozenOpeningBalanceAreaRequest(
    Guid FarmingAreaId,
    string Reason);

public record FrozenInventoryOpeningBalanceDto(
    Guid Id,
    Guid FrozenLotId,
    Guid? FarmingAreaId,
    int OpeningQuantity,
    decimal OpeningWeightKg,
    DateTime OpeningDate,
    string Source,
    string VerificationStatus,
    Guid? VerifiedByUserId,
    DateTime? VerifiedAt,
    string? VerificationNote,
    int RecordedQuantityDelta,
    decimal RecordedWeightDeltaKg,
    int ExpectedCurrentQuantity,
    decimal ExpectedCurrentWeightKg,
    int ActualCurrentQuantity,
    decimal ActualCurrentWeightKg,
    bool IsReconciled);