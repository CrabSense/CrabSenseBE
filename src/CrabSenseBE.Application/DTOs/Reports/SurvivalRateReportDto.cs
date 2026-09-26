namespace CrabSenseBE.Application.DTOs.Reports;

/// <summary>
/// Báo cáo tổng quan tình trạng cua.
/// </summary>
public record SurvivalRateReportDto
(
    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    int HarvestedCrabs,

    decimal SurvivalRate,
    decimal MortalityRate,
    decimal HarvestRate,

    List<SurvivalByAreaDto> ByAreas,
    List<SurvivalTrendDto> Trend               // Xu hướng theo thời gian
);

/// <summary>
/// Báo cáo tỷ lệ sống theo khu vực.
/// </summary>
public record SurvivalByAreaDto
(
    Guid AreaId,
    string AreaName,

    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    int HarvestedCrabs,

    decimal SurvivalRate,
    decimal MortalityRate,
    decimal HarvestRate
);

/// <summary>
/// Điều kiện lọc báo cáo tỷ lệ sống.
/// </summary>
public record SurvivalRateFilterDto
(
    Guid? AreaId,
    DateTime? FromDate,
    DateTime? ToDate
);

/// <summary>Dữ liệu xu hướng tỷ lệ sống theo ngày.</summary>
public record SurvivalTrendDto
(
    DateTime Date,
    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    int HarvestedCrabs,
    decimal SurvivalRate
);
