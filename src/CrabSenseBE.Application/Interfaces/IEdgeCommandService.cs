using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IEdgeCommandService
{
    Task<ApiResponse<EdgeCommandDto>> EnqueueManualAsync(
        EnqueueEdgeCommandRequest request,
        Guid actorId,
        CancellationToken ct = default);

    Task<ApiResponse<EdgeCommandDto>> EnqueueAutomationAsync(
        EnqueueEdgeCommandRequest request,
        Guid farmingAreaId,
        string automationSource,
        CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<EdgeCommandDto>>> GetPendingAsync(
        string deviceCode,
        string leaseHolder,
        CancellationToken ct = default);

    Task<ApiResponse<EdgeCommandDto>> AcknowledgeAsync(
        Guid id,
        EdgeCommandAckRequest request,
        string authenticatedDeviceCode,
        CancellationToken ct = default);
}
