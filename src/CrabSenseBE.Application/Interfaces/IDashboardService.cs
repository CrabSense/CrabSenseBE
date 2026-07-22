using CrabSenseBE.Application.DTOs.Dashboard;

namespace CrabSenseBE.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardOverviewAsync(
        CancellationToken cancellationToken = default);
}