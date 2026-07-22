using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

public interface ISurvivalRateReportService
{
    /// <summary>
    /// Lấy báo cáo tỷ lệ sống tổng quan.
    /// </summary>
    Task<SurvivalRateReportDto>
        GetSurvivalRateReportAsync(
            SurvivalRateFilterDto filter,
            CancellationToken cancellationToken = default);
}