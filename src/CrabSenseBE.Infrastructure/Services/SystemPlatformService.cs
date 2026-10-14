using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.SystemPlatform;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.Infrastructure.Services;

public class SystemPlatformService : ISystemPlatformService
{
    private static readonly string[] RecordingModes =
        ["Continuous", "MotionDetection", "AiEvent", "Scheduled", "Disabled"];

    private readonly AppDbContext _db;
    private readonly IDatabaseBackupService _backups;
    private readonly IConfiguration _config;

    public SystemPlatformService(AppDbContext db, IDatabaseBackupService backups, IConfiguration config)
    {
        _db = db;
        _backups = backups;
        _config = config;
    }

    public async Task<ApiResponse<SystemHealthDto>> GetHealthAsync(CancellationToken ct = default)
    {
        var database = await _db.Database.CanConnectAsync(ct);
        return ApiResponse<SystemHealthDto>.Ok(new SystemHealthDto(
            database ? "Healthy" : "Degraded",
            database,
            DateTime.UtcNow));
    }

    public async Task<ApiResponse<IReadOnlyList<SystemServiceDto>>> GetServicesAsync(CancellationToken ct = default)
    {
        var database = await _db.Database.CanConnectAsync(ct);
        var drive = CurrentDrive();
        var aiRunning = await _db.AiManagedServices.CountAsync(x => x.Status == "Running", ct);
        var aiTotal = await _db.AiManagedServices.CountAsync(ct);
        var mqtt = await ProbeMqttAsync(ct);

        var services = new List<SystemServiceDto>
        {
            new("API Server", "Running", $"PID {Environment.ProcessId}"),
            new("Database", database ? "Running" : "Error", database ? "PostgreSQL connected" : "Cannot connect"),
            new("Storage Service", drive?.IsReady == true ? "Running" : "Warning", drive?.Name),
            new("AI Inference Service",
                aiTotal == 0 ? "NotConfigured" : aiRunning > 0 ? "Running" : "Stopped",
                aiTotal == 0 ? "Chưa đăng ký dịch vụ AI" : $"{aiRunning}/{aiTotal} running"),
            mqtt,
        };
        return ApiResponse<IReadOnlyList<SystemServiceDto>>.Ok(services);
    }

    public async Task<ApiResponse<SystemMetricsDto>> GetMetricsAsync(CancellationToken ct = default)
    {
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var wallStart = Stopwatch.GetTimestamp();
        await Task.Delay(150, ct);
        process.Refresh();
        var elapsedMs = Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds;
        var cpu = elapsedMs <= 0
            ? 0
            : (process.TotalProcessorTime - cpuStart).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100;

        var memory = GC.GetGCMemoryInfo();
        long received = 0;
        long sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var stats = nic.GetIPStatistics();
            received += stats.BytesReceived;
            sent += stats.BytesSent;
        }

        var drive = CurrentDrive();
        return ApiResponse<SystemMetricsDto>.Ok(new SystemMetricsDto(
            Math.Round(Math.Clamp(cpu, 0, 100), 1),
            process.WorkingSet64,
            memory.TotalAvailableMemoryBytes,
            drive is { IsReady: true } ? drive.TotalSize - drive.AvailableFreeSpace : 0,
            drive is { IsReady: true } ? drive.TotalSize : 0,
            received,
            sent,
            DateTime.UtcNow));
    }

    public async Task<ApiResponse<IReadOnlyList<SystemVersionDto>>> GetVersionsAsync(CancellationToken ct = default)
    {
        var apiVersion = typeof(SystemPlatformService).Assembly.GetName().Version?.ToString() ?? "unknown";
        var latest = await _db.SystemDeployments
            .AsNoTracking()
            .OrderByDescending(x => x.DeployedAt)
            .ToListAsync(ct);

        var rows = new List<SystemVersionDto>
        {
            new("Backend API", apiVersion, "Running", "assembly"),
        };
        foreach (var group in latest.GroupBy(x => x.Component))
        {
            var current = group.First();
            if (string.Equals(current.Component, "Backend API", StringComparison.OrdinalIgnoreCase))
                continue;
            rows.Add(new SystemVersionDto(current.Component, current.Version, current.Status, "deployment"));
        }
        return ApiResponse<IReadOnlyList<SystemVersionDto>>.Ok(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<SystemDeploymentDto>>> GetDeploymentsAsync(CancellationToken ct = default)
    {
        var rows = await _db.SystemDeployments.AsNoTracking()
            .OrderByDescending(x => x.DeployedAt)
            .Take(100)
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SystemDeploymentDto>>.Ok(rows.Select(MapDeployment).ToList());
    }

    public async Task<ApiResponse<SystemDeploymentDto>> DeployAsync(
        DeployRequest request, Guid? userId, string? actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Component) || string.IsNullOrWhiteSpace(request.Version))
            throw AppException.BadRequest("Component và version là bắt buộc.");

        var row = new SystemDeployment
        {
            Component = request.Component.Trim(),
            Version = request.Version.Trim(),
            Environment = string.IsNullOrWhiteSpace(request.Environment) ? "Production" : request.Environment.Trim(),
            Status = "Recorded",
            DeployedBy = actor,
            DeployedByUserId = userId,
            DeployedAt = DateTime.UtcNow,
        };
        _db.SystemDeployments.Add(row);
        await AuditAsync(userId, "Deploy", "SystemDeployment", row.Id, $"{row.Component} {row.Version}", null, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SystemDeploymentDto>.Ok(MapDeployment(row));
    }

    public async Task<ApiResponse<SystemDeploymentDto>> RollbackAsync(
        RollbackRequest request, Guid? userId, string? actor, CancellationToken ct = default)
    {
        var history = await _db.SystemDeployments
            .Where(x => x.Component == request.Component && x.Environment == request.Environment)
            .OrderByDescending(x => x.DeployedAt)
            .Take(2)
            .ToListAsync(ct);
        if (history.Count < 2)
            throw AppException.BadRequest("Không có phiên bản trước để rollback.");

        var previous = history[1];
        var row = new SystemDeployment
        {
            Component = previous.Component,
            Version = previous.Version,
            Environment = previous.Environment,
            Status = "RolledBack",
            DeployedBy = actor,
            DeployedByUserId = userId,
            DeployedAt = DateTime.UtcNow,
        };
        _db.SystemDeployments.Add(row);
        await AuditAsync(userId, "Rollback", "SystemDeployment", row.Id, $"{row.Component} {row.Version}", null, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SystemDeploymentDto>.Ok(MapDeployment(row));
    }

    public async Task<ApiResponse<SystemDeploymentDto>> RequestRestartAsync(
        string component, Guid? userId, string? actor, CancellationToken ct = default)
    {
        var row = new SystemDeployment
        {
            Component = string.IsNullOrWhiteSpace(component) ? "Backend API" : component.Trim(),
            Version = typeof(SystemPlatformService).Assembly.GetName().Version?.ToString() ?? "unknown",
            Environment = "Production",
            Status = "RestartRequested",
            DeployedBy = actor,
            DeployedByUserId = userId,
            DeployedAt = DateTime.UtcNow,
        };
        _db.SystemDeployments.Add(row);
        await AuditAsync(userId, "RestartRequested", "SystemDeployment", row.Id, row.Component, null, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SystemDeploymentDto>.Ok(MapDeployment(row), "Đã ghi yêu cầu restart. Tiến trình API không bị tắt.");
    }

    public async Task<ApiResponse<DeviceActionResultDto>> RestartDeviceAsync(
        Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var device = await RequireDevice(deviceId, ct);
        _db.EdgeCommands.Add(new EdgeCommand
        {
            DeviceCode = device.DeviceCode,
            Command = "restart",
            Status = "pending",
        });
        await AuditAsync(userId, "RestartDevice", "Device", device.Id, device.DeviceCode, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<DeviceActionResultDto>.Ok(new DeviceActionResultDto(device.Id, device.Status.ToString(), "Đã xếp lệnh restart."));
    }

    public async Task<ApiResponse<DeviceActionResultDto>> UpdateFirmwareAsync(
        Guid deviceId, UpdateFirmwareRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FirmwareVersion))
            throw AppException.BadRequest("Firmware version là bắt buộc.");
        var device = await RequireDevice(deviceId, ct);
        device.FirmwareVersion = request.FirmwareVersion.Trim();
        await AuditAsync(userId, "UpdateFirmware", "Device", device.Id, device.FirmwareVersion, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<DeviceActionResultDto>.Ok(new DeviceActionResultDto(device.Id, device.Status.ToString(), device.FirmwareVersion));
    }

    public async Task<ApiResponse<DeviceActionResultDto>> DisableDeviceAsync(
        Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var device = await RequireDevice(deviceId, ct);
        device.Status = DeviceStatus.Maintenance;
        await AuditAsync(userId, "DisableDevice", "Device", device.Id, device.DeviceCode, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<DeviceActionResultDto>.Ok(new DeviceActionResultDto(device.Id, device.Status.ToString(), "Thiết bị chuyển sang Maintenance."));
    }

    public async Task<ApiResponse<DeviceDiagnosticsDto>> GetDeviceDiagnosticsAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await RequireDevice(deviceId, ct);
        var sensorIds = await _db.Sensors.Where(s => s.DeviceId == deviceId).Select(s => s.Id).ToListAsync(ct);
        var latest = sensorIds.Count == 0
            ? null
            : await _db.WaterMeasurements
                .Where(m => sensorIds.Contains(m.SensorId))
                .MaxAsync(m => (DateTime?)m.MeasuredAt, ct);
        var pending = await _db.EdgeCommands.CountAsync(
            c => c.DeviceCode == device.DeviceCode && c.Status == "pending", ct);
        var recent = sensorIds.Count == 0
            ? new List<DateTime>()
            : await _db.WaterMeasurements.AsNoTracking()
                .Where(m => sensorIds.Contains(m.SensorId))
                .OrderByDescending(m => m.MeasuredAt)
                .Select(m => m.MeasuredAt)
                .Take(8)
                .ToListAsync(ct);
        return ApiResponse<DeviceDiagnosticsDto>.Ok(new DeviceDiagnosticsDto(
            device.Id, device.DeviceCode, device.Status.ToString(), device.LastSeenAt,
            device.BatteryLevel, device.RssiDbm, device.IpAddress, device.FirmwareVersion,
            sensorIds.Count, pending, latest, recent));
    }

    public async Task<ApiResponse<DeviceConnectionTestDto>> TestDeviceConnectionAsync(
        Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var device = await RequireDevice(deviceId, ct);
        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            await AuditAsync(userId, "TestConnection", "Device", device.Id, "No IP", ip, ct);
            await _db.SaveChangesAsync(ct);
            return ApiResponse<DeviceConnectionTestDto>.Ok(new DeviceConnectionTestDto(
                device.Id, false, "Thiết bị chưa có địa chỉ IP để kiểm tra.", null, null, device.LastSeenAt, device.RssiDbm));
        }

        var port = await ProbePortAsync(device.IpAddress, ct);
        var message = port is int open
            ? $"Đã kết nối TCP tới {device.IpAddress}:{open}."
            : $"Không mở được cổng 80 hoặc 554 trên {device.IpAddress}. Heartbeat gần nhất vẫn được giữ nếu thiết bị còn gửi dữ liệu.";
        await AuditAsync(userId, "TestConnection", "Device", device.Id, message, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<DeviceConnectionTestDto>.Ok(new DeviceConnectionTestDto(
            device.Id, port.HasValue, message, device.IpAddress, port, device.LastSeenAt, device.RssiDbm));
    }

    private static async Task<int?> ProbePortAsync(string ipAddress, CancellationToken ct)
    {
        foreach (var port in new[] { 80, 554 })
        {
            using var client = new TcpClient();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(ipAddress, port, timeout.Token);
                return port;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
            }
        }
        return null;
    }

    public async Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetDeviceLogsAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await RequireDevice(deviceId, ct);
        var sensorIds = await _db.Sensors.Where(s => s.DeviceId == deviceId).Select(s => s.Id).ToListAsync(ct);
        var logs = await _db.OperationLogs.AsNoTracking()
            .Where(x => x.EntityId == deviceId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        var alerts = await _db.Alerts.AsNoTracking()
            .Where(a => a.SensorId != null && sensorIds.Contains(a.SensorId.Value))
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var rows = logs.Select(x => new SystemLogDto(x.Id, x.CreatedAt, "INFO", "device", x.Action + " " + (x.Details ?? ""), null, device.DeviceCode, x.IpAddress))
            .Concat(alerts.Select(a => new SystemLogDto(a.Id, a.CreatedAt, a.Severity.ToString(), "alert", a.Message, null, device.DeviceCode, null)))
            .OrderByDescending(x => x.At)
            .ToList();
        return ApiResponse<IReadOnlyList<SystemLogDto>>.Ok(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<CameraRecordingDto>>> GetRecordingSettingsAsync(CancellationToken ct = default)
    {
        var devices = await CameraDevices(ct);
        var settings = await _db.CameraRecordingSettings.AsNoTracking().ToListAsync(ct);
        var rows = devices.Select(device =>
        {
            var setting = settings.FirstOrDefault(x => x.DeviceId == device.Id);
            return MapCamera(device, setting);
        }).ToList();
        return ApiResponse<IReadOnlyList<CameraRecordingDto>>.Ok(rows);
    }

    public async Task<ApiResponse<CameraRecordingDto>> UpsertRecordingAsync(
        Guid deviceId, UpsertCameraRecordingRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (!RecordingModes.Contains(request.Mode))
            throw AppException.BadRequest("Mode không hợp lệ.");
        var device = await RequireDevice(deviceId, ct);
        var setting = await _db.CameraRecordingSettings.FirstOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (setting is null)
        {
            setting = new CameraRecordingSetting { DeviceId = deviceId };
            _db.CameraRecordingSettings.Add(setting);
        }
        setting.Mode = request.Mode;
        setting.RetentionDays = request.RetentionDays;
        setting.VideoQuality = request.VideoQuality;
        setting.Fps = request.Fps;
        setting.MaxStorageGb = request.MaxStorageGb;
        setting.WeeklyScheduleJson = request.WeeklyScheduleJson;
        await AuditAsync(userId, "UpdateRecording", "Device", deviceId, request.Mode, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<CameraRecordingDto>.Ok(MapCamera(device, setting));
    }

    public async Task<ApiResponse<IReadOnlyList<RecordingHistoryDto>>> GetRecordingHistoryAsync(Guid? deviceId, CancellationToken ct = default)
    {
        var query = _db.MediaAssets.AsNoTracking().Where(x =>
            x.Category.ToLower().Contains("video") || x.ContentType.StartsWith("video/"));
        if (deviceId.HasValue)
            query = query.Where(x => x.DeviceId == deviceId);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<RecordingHistoryDto>>.Ok(rows.Select(x =>
            new RecordingHistoryDto(x.Id, x.DeviceId, x.FileName, x.Category, x.SizeBytes, x.CreatedAt)).ToList());
    }

    public async Task<ApiResponse<IReadOnlyList<AiServiceDto>>> GetAiServicesAsync(CancellationToken ct = default)
    {
        var services = await _db.AiManagedServices.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var counts = await _db.AiDetections.AsNoTracking()
            .GroupBy(x => x.ModelVersion)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AiServiceDto>>.Ok(services.Select(service =>
        {
            var count = counts.FirstOrDefault(x => x.Key == service.Version)?.Count ?? 0;
            return MapAi(service, count);
        }).ToList());
    }

    public async Task<ApiResponse<AiServiceDto>> CreateAiServiceAsync(
        UpsertAiServiceRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = new AiManagedService
        {
            Name = request.Name.Trim(),
            ModelName = request.ModelName.Trim(),
            Version = request.Version.Trim(),
            DeviceLabel = request.DeviceLabel,
            ConfidenceThreshold = request.ConfidenceThreshold,
            FrameSamplingFps = request.FrameSamplingFps,
            BatchSize = request.BatchSize,
            DetectionClassesJson = string.IsNullOrWhiteSpace(request.DetectionClassesJson) ? "[]" : request.DetectionClassesJson,
            Status = "Stopped",
        };
        _db.AiManagedServices.Add(row);
        await AuditAsync(userId, "CreateAiService", "AiManagedService", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(MapAi(row, 0));
    }

    public async Task<ApiResponse<AiServiceDto>> RestartAiServiceAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await RequireAi(id, ct);
        row.Status = "Running";
        row.StartedAt = DateTime.UtcNow;
        await AuditAsync(userId, "RestartAiService", "AiManagedService", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(MapAi(row, 0));
    }

    public async Task<ApiResponse<AiServiceDto>> StopAiServiceAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await RequireAi(id, ct);
        row.Status = "Stopped";
        await AuditAsync(userId, "StopAiService", "AiManagedService", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(MapAi(row, 0));
    }

    public async Task<ApiResponse<AiServiceDto>> DeployAiModelAsync(
        Guid id, DeployModelRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
            throw AppException.BadRequest("Version là bắt buộc.");
        var row = await RequireAi(id, ct);
        row.PreviousVersion = row.Version;
        row.Version = request.Version.Trim();
        row.Status = "Running";
        row.StartedAt = DateTime.UtcNow;
        await AuditAsync(userId, "DeployAiModel", "AiManagedService", row.Id, row.Version, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(MapAi(row, 0));
    }

    public async Task<ApiResponse<AiServiceDto>> RollbackAiModelAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await RequireAi(id, ct);
        if (string.IsNullOrWhiteSpace(row.PreviousVersion))
            throw AppException.BadRequest("Không có version trước.");
        (row.Version, row.PreviousVersion) = (row.PreviousVersion, row.Version);
        await AuditAsync(userId, "RollbackAiModel", "AiManagedService", row.Id, row.Version, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(MapAi(row, 0));
    }

    public Task<ApiResponse<StorageCapacityDto>> GetStorageCapacityAsync(CancellationToken ct = default)
    {
        var drive = CurrentDrive();
        if (drive is not { IsReady: true })
            throw AppException.BadRequest("Không đọc được ổ đĩa của máy chủ API.");
        return Task.FromResult(ApiResponse<StorageCapacityDto>.Ok(new StorageCapacityDto(
            drive.TotalSize,
            drive.TotalSize - drive.AvailableFreeSpace,
            drive.AvailableFreeSpace,
            drive.Name)));
    }

    public async Task<ApiResponse<IReadOnlyList<StorageUsageItemDto>>> GetStorageUsageAsync(CancellationToken ct = default)
    {
        var media = await _db.MediaAssets.AsNoTracking()
            .GroupBy(x => x.Category)
            .Select(g => new StorageUsageItemDto(g.Key, g.Sum(x => x.SizeBytes), g.Count()))
            .ToListAsync(ct);
        var backupBytes = _backups.List().Sum(x => x.SizeBytes);
        var backupCount = _backups.List().Count;
        media.Add(new StorageUsageItemDto("database-backup", backupBytes, backupCount));
        return ApiResponse<IReadOnlyList<StorageUsageItemDto>>.Ok(media);
    }

    public async Task<ApiResponse<IReadOnlyList<StorageUsagePointDto>>> GetStorageHistoryAsync(int days, CancellationToken ct = default)
    {
        var from = DateTime.UtcNow.Date.AddDays(-Math.Clamp(days, 1, 90));
        var rows = await _db.MediaAssets.AsNoTracking()
            .Where(x => x.CreatedAt >= from)
            .Select(x => new { x.CreatedAt, x.SizeBytes })
            .ToListAsync(ct);
        var points = rows
            .GroupBy(x => x.CreatedAt.Date)
            .Select(g => new StorageUsagePointDto(g.Key, g.Sum(x => x.SizeBytes)))
            .OrderBy(x => x.Day)
            .ToList();
        return ApiResponse<IReadOnlyList<StorageUsagePointDto>>.Ok(points);
    }

    public async Task<ApiResponse<BackupScheduleDto>> GetBackupScheduleAsync(CancellationToken ct = default)
    {
        var row = await _db.BackupScheduleSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return ApiResponse<BackupScheduleDto>.Ok(row is null
            ? new BackupScheduleDto(false, "Daily", "02:00", 30)
            : new BackupScheduleDto(row.Enabled, row.Frequency, row.BackupTime, row.RetentionDays));
    }

    public async Task<ApiResponse<BackupScheduleDto>> UpdateBackupScheduleAsync(
        BackupScheduleDto request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await _db.BackupScheduleSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new BackupScheduleSetting();
            _db.BackupScheduleSettings.Add(row);
        }
        row.Enabled = request.Enabled;
        row.Frequency = request.Frequency;
        row.BackupTime = request.BackupTime;
        row.RetentionDays = request.RetentionDays;
        await AuditAsync(userId, "UpdateBackupSchedule", "BackupSchedule", row.Id, request.BackupTime, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<BackupScheduleDto>.Ok(request);
    }

    public async Task<ApiResponse<SecurityOverviewDto>> GetSecurityOverviewAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var failed = await _db.OperationLogs.CountAsync(
            x => x.Action == "LoginFailed" && x.CreatedAt >= since, ct);
        var sessions = await _db.AppUsers.CountAsync(x => x.RefreshTokenExpiry > DateTime.UtcNow && x.IsActive, ct);
        var keys = await _db.ServiceApiKeys.CountAsync(x => x.RevokedAt == null, ct);
        var alerts = await _db.Alerts.CountAsync(x => x.Status == AlertStatus.Active, ct);
        return ApiResponse<SecurityOverviewDto>.Ok(new SecurityOverviewDto(failed, sessions, keys, alerts));
    }

    public async Task<ApiResponse<IReadOnlyList<UserSessionDto>>> GetSessionsAsync(CancellationToken ct = default)
    {
        var rows = await _db.AppUsers.AsNoTracking()
            .Where(x => x.RefreshTokenExpiry > DateTime.UtcNow && x.IsActive)
            .OrderByDescending(x => x.LastLoginAt)
            .ToListAsync(ct);
        return ApiResponse<IReadOnlyList<UserSessionDto>>.Ok(rows.Select(x => new UserSessionDto(
            x.Id, x.Username, x.Role.ToString(), x.LastLoginIp, x.LastUserAgent, x.LastLoginAt)).ToList());
    }

    public async Task<ApiResponse> TerminateSessionAsync(Guid userId, Guid? actorId, string? ip, CancellationToken ct = default)
    {
        var user = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw AppException.NotFound("User");
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await AuditAsync(actorId, "TerminateSession", "AppUser", user.Id, user.Username, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok("Đã kết thúc phiên.");
    }

    public async Task<ApiResponse<IReadOnlyList<ApiKeyDto>>> GetApiKeysAsync(CancellationToken ct = default)
    {
        var rows = await _db.ServiceApiKeys.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<ApiKeyDto>>.Ok(rows.Select(MapKey).ToList());
    }

    public async Task<ApiResponse<CreateApiKeyResultDto>> CreateApiKeyAsync(
        CreateApiKeyRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw AppException.BadRequest("Tên API key là bắt buộc.");
        var (entity, plain) = NewKey(request.Name.Trim());
        _db.ServiceApiKeys.Add(entity);
        await AuditAsync(userId, "CreateApiKey", "ServiceApiKey", entity.Id, entity.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<CreateApiKeyResultDto>.Ok(new CreateApiKeyResultDto(entity.Id, entity.Name, entity.Prefix, plain, entity.CreatedAt));
    }

    public async Task<ApiResponse<CreateApiKeyResultDto>> RotateApiKeyAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await _db.ServiceApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("API key");
        var plain = "cs_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        row.Prefix = plain[..10];
        row.KeyHash = Hash(plain);
        row.RevokedAt = null;
        row.LastUsedAt = null;
        await AuditAsync(userId, "RotateApiKey", "ServiceApiKey", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<CreateApiKeyResultDto>.Ok(new CreateApiKeyResultDto(row.Id, row.Name, row.Prefix, plain, row.CreatedAt));
    }

    public async Task<ApiResponse> RevokeApiKeyAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await _db.ServiceApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("API key");
        row.RevokedAt = DateTime.UtcNow;
        await AuditAsync(userId, "RevokeApiKey", "ServiceApiKey", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse.Ok("Đã thu hồi API key.");
    }

    public async Task<ApiResponse<SecurityPolicyDto>> GetPolicyAsync(CancellationToken ct = default)
    {
        var row = await _db.SecurityPolicySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return ApiResponse<SecurityPolicyDto>.Ok(row is null
            ? new SecurityPolicyDto(30, 5, true)
            : new SecurityPolicyDto(row.SessionDays, row.FailedLoginAlertThreshold, row.RequireConfirmRestore));
    }

    public async Task<ApiResponse<SecurityPolicyDto>> UpdatePolicyAsync(
        SecurityPolicyDto request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await _db.SecurityPolicySettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new SecurityPolicySetting();
            _db.SecurityPolicySettings.Add(row);
        }
        row.SessionDays = request.SessionDays;
        row.FailedLoginAlertThreshold = request.FailedLoginAlertThreshold;
        row.RequireConfirmRestore = request.RequireConfirmRestore;
        await AuditAsync(userId, "UpdateSecurityPolicy", "SecurityPolicy", row.Id, null, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SecurityPolicyDto>.Ok(request);
    }

    public async Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetLogsAsync(
        string? source, string? severity, string? search, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var logs = new List<SystemLogDto>();
        if (source is null or "" or "system" or "audit")
        {
            var query = _db.OperationLogs.AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(x => x.CreatedAt >= from);
            if (to.HasValue) query = query.Where(x => x.CreatedAt <= to);
            var rows = await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
            logs.AddRange(rows.Select(x => new SystemLogDto(
                x.Id, x.CreatedAt, "INFO", "system", $"{x.Action} {x.Details}".Trim(), x.UserId.ToString(), x.EntityType, x.IpAddress)));
        }
        if (source is null or "" or "device" or "alert")
        {
            var query = _db.Alerts.AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(x => x.CreatedAt >= from);
            if (to.HasValue) query = query.Where(x => x.CreatedAt <= to);
            var rows = await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
            logs.AddRange(rows.Select(a => new SystemLogDto(
                a.Id, a.CreatedAt, a.Severity.ToString(), "alert", a.Message, null, a.SensorId?.ToString(), null)));
        }
        if (source is null or "" or "ai")
        {
            var rows = await _db.AiDetections.AsNoTracking()
                .OrderByDescending(x => x.DetectedAt)
                .Take(200)
                .ToListAsync(ct);
            logs.AddRange(rows.Select(x => new SystemLogDto(
                x.Id, x.DetectedAt, x.Status == "failed" ? "ERROR" : "INFO", "ai",
                $"{x.DetectionType} {x.ModelVersion}", null, x.DeviceId?.ToString(), null)));
        }
        if (source is null or "" or "security")
        {
            var rows = await _db.OperationLogs.AsNoTracking()
                .Where(x => x.Action == "Login" || x.Action == "LoginFailed")
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync(ct);
            logs.AddRange(rows.Select(x => new SystemLogDto(
                x.Id, x.CreatedAt, x.Action == "LoginFailed" ? "WARNING" : "INFO", "security",
                x.Action, x.Details, null, x.IpAddress)));
        }

        IEnumerable<SystemLogDto> filtered = logs;
        if (!string.IsNullOrWhiteSpace(severity))
            filtered = filtered.Where(x => string.Equals(x.Severity, severity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(x =>
                x.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (x.Actor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        return ApiResponse<IReadOnlyList<SystemLogDto>>.Ok(filtered.OrderByDescending(x => x.At).Take(400).ToList());
    }

    public async Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetAuditLogsAsync(CancellationToken ct = default)
    {
        var rows = await _db.OperationLogs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(300)
            .ToListAsync(ct);
        var users = await _db.AppUsers.AsNoTracking().Select(x => new { x.Id, x.Username }).ToListAsync(ct);
        var mapped = rows.Select(x => new SystemLogDto(
            x.Id,
            x.CreatedAt,
            "INFO",
            "audit",
            x.Action,
            users.FirstOrDefault(u => u.Id == x.UserId)?.Username,
            x.EntityType,
            x.IpAddress)).ToList();
        return ApiResponse<IReadOnlyList<SystemLogDto>>.Ok(mapped);
    }

    private async Task<Device> RequireDevice(Guid id, CancellationToken ct)
        => await _db.Devices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Device");

    private async Task<AiManagedService> RequireAi(Guid id, CancellationToken ct)
        => await _db.AiManagedServices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("AI service");

    private async Task<List<Device>> CameraDevices(CancellationToken ct)
    {
        var devices = await _db.Devices.AsNoTracking().ToListAsync(ct);
        return devices.Where(device =>
            device.DeviceType.Contains("camera", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(device.StreamUrl)
            || !string.IsNullOrWhiteSpace(device.SnapshotUrl)).ToList();
    }

    private async Task AuditAsync(Guid? userId, string action, string entityType, Guid entityId, string? details, string? ip, CancellationToken ct)
    {
        if (userId is null || userId == Guid.Empty) return;
        _db.OperationLogs.Add(new OperationLog
        {
            UserId = userId.Value,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ip,
        });
        await Task.CompletedTask;
    }

    private static SystemDeploymentDto MapDeployment(SystemDeployment row) => new(
        row.Id, row.Component, row.Version, row.Environment, row.Status, row.DeployedBy, row.DeployedAt);

    private static CameraRecordingDto MapCamera(Device device, CameraRecordingSetting? setting) => new(
        setting?.Id, device.Id, device.DeviceCode, device.Name, device.InstallationLocation,
        device.Status.ToString(), device.Resolution, device.SnapshotUrl, device.StreamUrl,
        setting?.Mode ?? "Disabled", setting?.RetentionDays ?? 30, setting?.VideoQuality,
        setting?.Fps, setting?.MaxStorageGb, setting?.WeeklyScheduleJson);

    private static AiServiceDto MapAi(AiManagedService row, int detections) => new(
        row.Id, row.Name, row.ModelName, row.Version, row.PreviousVersion, row.Status, row.DeviceLabel,
        row.ConfidenceThreshold, row.FrameSamplingFps, row.BatchSize, row.DetectionClassesJson,
        row.StartedAt, detections);

    private static ApiKeyDto MapKey(ServiceApiKey row) => new(
        row.Id, row.Name, row.Prefix, row.CreatedAt, row.LastUsedAt, row.RevokedAt != null);

    private static (ServiceApiKey Entity, string Plain) NewKey(string name)
    {
        var plain = "cs_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        return (new ServiceApiKey
        {
            Name = name,
            Prefix = plain[..10],
            KeyHash = Hash(plain),
        }, plain);
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DriveInfo? CurrentDrive()
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(root)) return null;
        return DriveInfo.GetDrives().FirstOrDefault(drive =>
            string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SystemServiceDto> ProbeMqttAsync(CancellationToken ct)
    {
        var host = _config["Mqtt:Host"];
        if (string.IsNullOrWhiteSpace(host))
            return new SystemServiceDto("MQTT Broker", "NotConfigured", "Chưa có Mqtt:Host");
        var port = int.TryParse(_config["Mqtt:Port"], out var parsed) ? parsed : 1883;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, ct);
            return new SystemServiceDto("MQTT Broker", "Running", $"{host}:{port}");
        }
        catch (Exception ex)
        {
            return new SystemServiceDto("MQTT Broker", "Error", ex.Message);
        }
    }
}
