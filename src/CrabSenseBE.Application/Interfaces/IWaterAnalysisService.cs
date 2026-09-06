using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IWaterAnalysisService
{
    Task<ApiResponse<WaterAnalysisSnapshotDto>> GetSnapshotAsync(Guid areaId, CancellationToken ct = default);
    Task<ApiResponse<WaterAnalysisSnapshotDto>> StartAsync(Guid areaId, CancellationToken ct = default);
}
