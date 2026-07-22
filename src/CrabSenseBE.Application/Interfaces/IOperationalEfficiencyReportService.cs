using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

public interface IOperationalEfficiencyReportService
{
    Task<OperationalEfficiencyDto> GetOperationalEfficiencyReportAsync(
        OperationalEfficiencyFilterDto filter,
        CancellationToken cancellationToken = default);
}