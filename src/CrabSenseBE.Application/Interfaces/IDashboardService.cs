using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Dashboard;

namespace CrabSenseBE.Application.Interfaces;

public interface IDashboardService
{
    Task<ApiResponse<DashboardOverviewDto>> GetOverviewAsync(Guid? farmingAreaId = null, CancellationToken ct = default);
    Task<ApiResponse<DashboardMetricsDto>> GetMetricsAsync(Guid? farmingAreaId = null, CancellationToken ct = default);
    Task<ApiResponse<List<AiRecommendationDto>>> GetRecommendationsAsync(Guid? farmingAreaId = null, CancellationToken ct = default);
}

public interface IOperationLogService
{
    Task<ApiResponse<List<OperationTaskDto>>> GetTodayTasksAsync(Guid? farmingAreaId = null, CancellationToken ct = default);
    Task<ApiResponse<List<RecentActivityDto>>> GetRecentAsync(int limit = 20, Guid? farmingAreaId = null, CancellationToken ct = default);
}
