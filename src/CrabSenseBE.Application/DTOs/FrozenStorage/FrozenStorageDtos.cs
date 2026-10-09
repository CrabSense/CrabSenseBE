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
    DateTime? UpdatedAt
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
    string? StorageLocation
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
    string Grade);