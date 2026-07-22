using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

public interface IMoltingReportService
{
    Task<MoltingReportDto> GetMoltingReportAsync(
        CancellationToken cancellationToken = default);
}