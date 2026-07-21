using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

public interface ISurvivalRateReportService
{
    /// <summary>
    /// Lấy báo cáo tỷ lệ sống tổng quan.
    /// </summary>
    Task<SurvivalRateReportDto> GetSurvivalRateReportAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tỷ lệ sống theo đợt nuôi cụ thể.
    /// </summary>
    Task<SurvivalByCropBatchDto> GetSurvivalRateByCropBatchAsync(
        Guid cropBatchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy tỷ lệ sống theo khu vực cụ thể.
    /// </summary>
    Task<SurvivalByAreaDto> GetSurvivalRateByAreaAsync(
        Guid areaId,
        CancellationToken cancellationToken = default);
}