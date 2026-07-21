namespace CrabSenseBE.Application.DTOs.Reports;

// 1. DTO tổng quan tỷ lệ sống
public record SurvivalRateReportDto
(
    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    decimal SurvivalRate,
    decimal MortalityRate,
    List<SurvivalByCropBatchDto> ByCropBatches,
    List<SurvivalByAreaDto> ByAreas
);

// 2. DTO chi tiết theo đợt nuôi
public record SurvivalByCropBatchDto
(
    Guid CropBatchId,
    string BatchCode,
    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    decimal SurvivalRate
);

// 3. DTO chi tiết theo khu vực
public record SurvivalByAreaDto
(
    Guid AreaId,
    string AreaName,
    int TotalCrabs,
    int AliveCrabs,
    int DeadCrabs,
    decimal SurvivalRate
);