using CrabSenseBE.Application.DTOs.Reports;

namespace CrabSenseBE.Application.Interfaces;

public interface ICrabLotComparisonReportService
{
    Task<IReadOnlyList<CrabLotCompareRowDto>> CompareAsync(
        Guid? farmingAreaId,
        CancellationToken ct = default);
}
