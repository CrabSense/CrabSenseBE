using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.SystemPlatform;

namespace CrabSenseBE.Application.Interfaces;

public interface ISystemPlatformService
{
    Task<ApiResponse<SystemHealthDto>> GetHealthAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<SystemServiceDto>>> GetServicesAsync(CancellationToken ct = default);
    Task<ApiResponse<SystemMetricsDto>> GetMetricsAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<SystemVersionDto>>> GetVersionsAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<SystemDeploymentDto>>> GetDeploymentsAsync(CancellationToken ct = default);
    Task<ApiResponse<SystemDeploymentDto>> DeployAsync(DeployRequest request, Guid? userId, string? actor, CancellationToken ct = default);
    Task<ApiResponse<SystemDeploymentDto>> RollbackAsync(RollbackRequest request, Guid? userId, string? actor, CancellationToken ct = default);
    Task<ApiResponse<SystemDeploymentDto>> RequestRestartAsync(string component, Guid? userId, string? actor, CancellationToken ct = default);

    Task<ApiResponse<DeviceActionResultDto>> RestartDeviceAsync(Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<DeviceActionResultDto>> UpdateFirmwareAsync(Guid deviceId, UpdateFirmwareRequest request, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<DeviceActionResultDto>> DisableDeviceAsync(Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<DeviceDiagnosticsDto>> GetDeviceDiagnosticsAsync(Guid deviceId, CancellationToken ct = default);
    Task<ApiResponse<DeviceConnectionTestDto>> TestDeviceConnectionAsync(Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetDeviceLogsAsync(Guid deviceId, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<CameraRecordingDto>>> GetRecordingSettingsAsync(CancellationToken ct = default);
    Task<ApiResponse<CameraRecordingDto>> UpsertRecordingAsync(Guid deviceId, UpsertCameraRecordingRequest request, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<RecordingHistoryDto>>> GetRecordingHistoryAsync(Guid? deviceId, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<AiServiceDto>>> GetAiServicesAsync(CancellationToken ct = default);
    Task<ApiResponse<AiServiceDto>> CreateAiServiceAsync(UpsertAiServiceRequest request, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<AiServiceDto>> RestartAiServiceAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<AiServiceDto>> StopAiServiceAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<AiServiceDto>> DeployAiModelAsync(Guid id, DeployModelRequest request, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<AiServiceDto>> RollbackAiModelAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default);

    Task<ApiResponse<StorageCapacityDto>> GetStorageCapacityAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<StorageUsageItemDto>>> GetStorageUsageAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<StorageUsagePointDto>>> GetStorageHistoryAsync(int days, CancellationToken ct = default);
    Task<ApiResponse<BackupScheduleDto>> GetBackupScheduleAsync(CancellationToken ct = default);
    Task<ApiResponse<BackupScheduleDto>> UpdateBackupScheduleAsync(BackupScheduleDto request, Guid? userId, string? ip, CancellationToken ct = default);

    Task<ApiResponse<SecurityOverviewDto>> GetSecurityOverviewAsync(CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<UserSessionDto>>> GetSessionsAsync(CancellationToken ct = default);
    Task<ApiResponse> TerminateSessionAsync(Guid userId, Guid? actorId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<ApiKeyDto>>> GetApiKeysAsync(CancellationToken ct = default);
    Task<ApiResponse<CreateApiKeyResultDto>> CreateApiKeyAsync(CreateApiKeyRequest request, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<CreateApiKeyResultDto>> RotateApiKeyAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse> RevokeApiKeyAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default);
    Task<ApiResponse<SecurityPolicyDto>> GetPolicyAsync(CancellationToken ct = default);
    Task<ApiResponse<SecurityPolicyDto>> UpdatePolicyAsync(SecurityPolicyDto request, Guid? userId, string? ip, CancellationToken ct = default);

    Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetLogsAsync(string? source, string? severity, string? search, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetAuditLogsAsync(CancellationToken ct = default);
}
