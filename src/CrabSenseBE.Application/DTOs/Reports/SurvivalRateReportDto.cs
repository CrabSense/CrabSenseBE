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
    List<SurvivalTrendDto> Trend,
    int? MissingCrabs = null,
    decimal? MissingRate = null
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
    decimal HarvestRate,
    int MissingCrabs = 0,
    decimal MissingRate = 0
);

/// <summary>
/// Điều kiện lọc báo cáo tỷ lệ sống.
/// </summary>
public record SurvivalRateFilterDto
(
    Guid? AreaId,
    DateTime? FromDate,
    DateTime? ToDate,
    Guid? CrabLotId = null
);

/// <summary>Dữ liệu xu hướng tỷ lệ sống theo ngày.</summary>
public record SurvivalTrendDto
(
    DateTime Date,
    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    int HarvestedCrabs,
    decimal SurvivalRate,
    int MissingCrabs = 0,
    decimal MissingRate = 0
);
