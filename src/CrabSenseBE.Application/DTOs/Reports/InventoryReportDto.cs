namespace CrabSenseBE.Application.DTOs.Reports;

/// <summary>
/// DTO dùng để trả về báo cáo tồn kho cua đông lạnh.
/// </summary>
public record InventoryReportDto
(
    // Tổng số lô đông lạnh hiện có
    int TotalInventoryLots,
    // Tổng khối lượng tồn kho, đơn vị: kg
    decimal TotalInventoryWeightKg,
    // Tổng số lượng cua
    int TotalInventoryQuantity,
    // Số lô đang Available
    int AvailableLots,
    // Số lô đã Reserved
    int ReservedLots,
    // Số lô đã Shipped
    int ShippedLots,
    // Số lô đã Expired
    int ExpiredLots,

    // Chi tiết tồn kho theo từng loại Grade
    IEnumerable<InventoryByGradeDto> ByGrade,
    // Thống kê theo thời gian
    IEnumerable<InventoryExpiryAlertDto> ExpiryAlerts,
    IEnumerable<InventoryTrendDto> Trend
);

/// <summary>
/// Thống kê tồn kho theo Grade cua.
/// Ví dụ: S, M, L.
/// </summary>
public record InventoryByGradeDto
(
    // Grade của cua
    string Grade,
    // Số lượng lô
    int LotCount,
    // Tổng số lượng cua
    int Quantity,
    // Tổng khối lượng
    decimal WeightKg
);

/// <summary>Cảnh báo lô sắp hết hạn.</summary>
public record InventoryExpiryAlertDto
(
    string LotCode,
    string Grade,
    decimal WeightKg,
    int Quantity,
    DateTime ExpiryDate,
    int DaysUntilExpiry,
    string Status          // "expired" | "expiring_soon" | "ok"
);

/// <summary>Xu hướng tồn kho theo ngày.</summary>
public record InventoryTrendDto
(
    DateTime Date,
    int LotCount,
    decimal WeightKg,
    int Quantity
);

/// <summary>Bộ lọc báo cáo tồn kho.</summary>
public record InventoryReportFilterDto
(
    DateTime? AsOfDate,         // Xem tồn kho tại thời điểm
    int? ExpiryWarningDays = 30 // Cảnh báo hết hạn trong X ngày
);