using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Dashboard;

namespace CrabSenseBE.Application.Interfaces;

public interface IDashboardService
{
    Task<ApiResponse<DashboardOverviewDto>> GetOverviewAsync(CancellationToken ct = default);
    Task<ApiResponse<DashboardMetricsDto>> GetMetricsAsync(CancellationToken ct = default);
    Task<ApiResponse<List<AiRecommendationDto>>> GetRecommendationsAsync(CancellationToken ct = default);
}

public interface IOperationLogService
{
    Task<ApiResponse<List<OperationTaskDto>>> GetTodayTasksAsync(CancellationToken ct = default);
    Task<ApiResponse<List<RecentActivityDto>>> GetRecentAsync(int limit = 20, CancellationToken ct = default);
}
