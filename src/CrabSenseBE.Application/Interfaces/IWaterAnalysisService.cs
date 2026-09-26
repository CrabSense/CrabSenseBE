using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;

namespace CrabSenseBE.Application.Interfaces;

public interface IWaterAnalysisService
{
    Task<ApiResponse<WaterAnalysisSnapshotDto>> GetSnapshotAsync(Guid areaId, CancellationToken ct = default);
    Task<ApiResponse<WaterAnalysisSnapshotDto>> StartAsync(
        Guid areaId, StartWaterAnalysisRequest? req, CancellationToken ct = default);
    Task<ApiResponse<WaterAnalysisSnapshotDto>> StopAsync(Guid areaId, CancellationToken ct = default);
    Task<ApiResponse<WaterAnalysisSnapshotDto>> UpdateSampleAsync(
        Guid areaId, UpdateWaterAnalysisSampleRequest req, CancellationToken ct = default);
    Task<ApiResponse<WaterAnalysisDetailDto>> GetDetailAsync(
        Guid areaId, Guid runId, CancellationToken ct = default);
}
