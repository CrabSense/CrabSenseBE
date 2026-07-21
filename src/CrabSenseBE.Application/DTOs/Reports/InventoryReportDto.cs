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
    IEnumerable<InventoryByGradeDto> ByGrade
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