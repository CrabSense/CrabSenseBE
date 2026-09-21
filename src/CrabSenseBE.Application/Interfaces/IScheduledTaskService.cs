using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Application.Interfaces;

public interface IScheduledTaskService
{
    Task<ApiResponse<IEnumerable<ScheduledTaskDto>>> ListAsync(
        Guid ownerId, Guid? farmingAreaId, CancellationToken ct = default);

    Task<ApiResponse<IEnumerable<ScheduledTaskDto>>> TodayAsync(
        Guid ownerId, Guid? farmingAreaId, CancellationToken ct = default);

    Task<ApiResponse<ScheduledTaskDto>> CreateAsync(
        Guid ownerId, CreateScheduledTaskRequest request, CancellationToken ct = default);

    Task<ApiResponse<ScheduledTaskDto>> UpdateAsync(
        Guid ownerId, Guid id, UpdateScheduledTaskRequest request, CancellationToken ct = default);

    Task<ApiResponse> DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    Task<ApiResponse<ScheduledTaskDto>> ToggleAsync(
        Guid ownerId, Guid id, bool enabled, CancellationToken ct = default);
}
