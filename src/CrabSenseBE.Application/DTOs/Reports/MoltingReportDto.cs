public record MoltingReportDto
(
    int TotalCrabs,
    int MoltingCrabs,       // cua đang trong giai đoạn lột (CrabStatus.Molting)
    int MoltedCrabs,        // cua đã lột xong (có MoltingRecord)
    decimal MoltingRate,    // MoltingRate = MoltedCrabs / TotalCrabs × 100
    List<MoltingByAreaDto> ByAreas,
    List<MoltingTrendDto> Trend       // Xu hướng theo thời gian
);

public record MoltingByAreaDto
(
    Guid AreaId,
    string AreaName,
    int TotalCrabs,
    int MoltingCrabs,
    int MoltedCrabs,
    decimal MoltingRate
);

/// <summary>Dữ liệu xu hướng lột xác theo ngày.</summary>
public record MoltingTrendDto
(
    DateTime Date,
    int MoltingCount,       // Số cua đang lột trong ngày
    int MoltedCount,        // Số cua đã lột xong trong ngày
    decimal MoltingRate     // Tỷ lệ lột trong ngày
);

/// <summary>Bộ lọc cho báo cáo molting.</summary>
public record MoltingReportFilterDto
(
    Guid? AreaId,
    DateTime? FromDate,
    DateTime? ToDate
);