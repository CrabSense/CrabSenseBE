using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IEdgeCommandService
{
    Task<ApiResponse<EdgeCommandDto>> EnqueueAsync(
        EnqueueEdgeCommandRequest request,
        Guid? actorId = null,
        CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<EdgeCommandDto>>> GetPendingAsync(
        string deviceCode,
        CancellationToken ct = default);

    Task<ApiResponse<EdgeCommandDto>> AcknowledgeAsync(
        Guid id,
        EdgeCommandAckRequest request,
        CancellationToken ct = default);
}
