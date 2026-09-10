using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IRasFlowService
{
    Task<ApiResponse<RasFlowDiagramDto>> GetDiagramByAreaAsync(Guid areaId, CancellationToken ct = default);
    Task<ApiResponse<RasFlowDiagramDto>> AddNodeAsync(Guid areaId, CreateRasFlowNodeRequest req, CancellationToken ct = default);
    Task<ApiResponse<RasFlowDiagramDto>> ReorderAsync(Guid areaId, ReorderRasFlowRequest req, CancellationToken ct = default);
    Task<ApiResponse> DeleteNodeAsync(Guid areaId, Guid nodeId, CancellationToken ct = default);
    Task<ApiResponse<RasFlowDiagramDto>> CommandAsync(
        Guid areaId, Guid nodeId, RasFlowCommandRequest req, Guid? actorId = null, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<RasComponentDto>>> ListComponentsAsync(Guid waterSystemId, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<WaterFlowDto>>> ListFlowsAsync(Guid waterSystemId, CancellationToken ct = default);
}
