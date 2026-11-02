using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

public interface IHarvestReportService
{
    Task<HarvestReportDto> GetHarvestReportAsync(
        HarvestReportFilterDto filter,
        CancellationToken cancellationToken = default);
    
    Task<HarvestPeriodReportDto> GetHarvestReportByPeriodAsync(
        HarvestReportFilterDto filter,
        CancellationToken cancellationToken = default);
}