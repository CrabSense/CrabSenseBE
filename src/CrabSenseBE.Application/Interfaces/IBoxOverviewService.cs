using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;

namespace CrabSenseBE.Application.Interfaces;

public interface IBoxOverviewService
{
    /// <summary>
    /// Enriched box list for Mobile Boxes tab (health, water, devices, alerts, AI, map layout).
    /// </summary>
    Task<ApiResponse<BoxesOverviewResponseDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        Guid? farmingRowId = null,
        string? status = null,
        CancellationToken ct = default);
}
