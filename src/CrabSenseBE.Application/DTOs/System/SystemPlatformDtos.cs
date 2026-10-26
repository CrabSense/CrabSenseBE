namespace CrabSenseBE.Application.DTOs.SystemPlatform;

public record SystemHealthDto(string Status, bool DatabaseConnected, DateTime CheckedAtUtc);

public record SystemServiceDto(string Name, string Status, string? Detail);

public record SystemMetricsDto(
    double CpuPercent,
    long RamUsedBytes,
    long RamTotalBytes,
    long StorageUsedBytes,
    long StorageTotalBytes,
    long NetworkBytesReceived,
    long NetworkBytesSent,
    DateTime SampledAtUtc);

public record SystemVersionDto(string Component, string Version, string Status, string Source);

public record DeployRequest(string Component, string Version, string Environment);

public record RollbackRequest(string Component, string Environment);

public record SystemDeploymentDto(
    Guid Id,
    string Component,
    string Version,
    string Environment,
    string Status,
    string? DeployedBy,
    DateTime DeployedAt);

public record DeviceActionResultDto(Guid DeviceId, string Status, string Message);

public record UpdateFirmwareRequest(string FirmwareVersion);

public record DeviceDiagnosticsDto(
    Guid DeviceId,
    string DeviceCode,
    string Status,
    DateTime? LastSeenAt,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    string? IpAddress,
    string? FirmwareVersion,
    int SensorCount,
    int PendingCommands,
    DateTime? LatestMeasurementAt,
    IReadOnlyList<DateTime> RecentMeasurements);

public record DeviceConnectionTestDto(
    Guid DeviceId,
    bool Reachable,
    string Message,
    string? IpAddress,
    int? Port,
    DateTime? LastSeenAt,
    decimal? RssiDbm);

public record SystemLogDto(
    Guid Id,
    DateTime At,
    string Severity,
    string Source,
    string Message,
    string? Actor,
    string? Target,
    string? IpAddress);

public record CameraRecordingDto(
    Guid? SettingId,
    Guid DeviceId,
    string DeviceCode,
    string? Name,
    string? AreaName,
    string? Status,
    string? Resolution,
    string? SnapshotUrl,
    string? StreamUrl,
    string Mode,
    int RetentionDays,
    string? VideoQuality,
    int? Fps,
    int? MaxStorageGb,
    string? WeeklyScheduleJson);

public record UpsertCameraRecordingRequest(
    string Mode,
    int RetentionDays,
    string? VideoQuality,
    int? Fps,
    int? MaxStorageGb,
    string? WeeklyScheduleJson);

public record RecordingHistoryDto(
    Guid Id,
    Guid? DeviceId,
    string FileName,
    string Category,
    long SizeBytes,
    DateTime CreatedAt);

public record AiServiceDto(
    Guid Id,
    string Name,
    string ModelName,
    string Version,
    string? PreviousVersion,
    string Status,
    string? DeviceLabel,
    decimal ConfidenceThreshold,
    decimal FrameSamplingFps,
    int BatchSize,
    string DetectionClassesJson,
    DateTime? StartedAt,
    int DetectionCount);

public record UpsertAiServiceRequest(
    string Name,
    string ModelName,
    string Version,
    string? DeviceLabel,
    decimal ConfidenceThreshold,
    decimal FrameSamplingFps,
    int BatchSize,
    string DetectionClassesJson);

public record DeployModelRequest(string Version);

public record StorageCapacityDto(
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    string DriveName);

public record StorageUsageItemDto(string Category, long Bytes, int Files);

public record StorageUsagePointDto(DateTime Day, long Bytes);

public record BackupScheduleDto(bool Enabled, string Frequency, string BackupTime, int RetentionDays);

public record SecurityOverviewDto(
    int FailedLogins,
    int ActiveSessions,
    int ApiKeys,
    int SecurityAlerts);

public record UserSessionDto(
    Guid UserId,
    string Username,
    string Role,
    string? IpAddress,
    string? UserAgent,
    DateTime? LastActivityAt);

public record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    bool Revoked);

public record CreateApiKeyResultDto(Guid Id, string Name, string Prefix, string Key, DateTime CreatedAt);

public record CreateApiKeyRequest(string Name);

public record SecurityPolicyDto(int SessionDays, int FailedLoginAlertThreshold, bool RequireConfirmRestore);
