using System.Diagnostics;
using System.Globalization;
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
        var aiTotal = await _db.AiManagedServices.CountAsync(ct);
        var mqtt = await ProbeMqttAsync(ct);

        var services = new List<SystemServiceDto>
        {
            new("API Server", "Running", $"PID {Environment.ProcessId}"),
            new("Database", database ? "Running" : "Error", database ? "PostgreSQL connected" : "Cannot connect"),
            new("Local Server Disk", drive?.IsReady == true ? "Running" : "Warning", drive?.Name),
            new("Media Storage", "NotVerified", "Provider health requires a real storage probe; local disk is not cloud storage."),
            new("AI Inference Service", aiTotal == 0 ? "NotConfigured" : "NotIntegrated",
                $"{aiTotal} registered configurations; runtime inference has not been verified."),
            mqtt,
        };
        return ApiResponse<IReadOnlyList<SystemServiceDto>>.Ok(services);
    }

    public async Task<ApiResponse<SystemMetricsDto>> GetMetricsAsync(CancellationToken ct = default)
    {
        using var process = Process.GetCurrentProcess();
        var hostCpuStart = ReadHostCpu();
        var cpuStart = process.TotalProcessorTime;
        var wallStart = Stopwatch.GetTimestamp();
        await Task.Delay(150, ct);
        process.Refresh();
        var elapsedMs = Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds;
        var cpu = elapsedMs <= 0
            ? 0
            : (process.TotalProcessorTime - cpuStart).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100;

        var hostCpuEnd = ReadHostCpu();
        double? hostCpu = null;
        if (hostCpuStart is { } start && hostCpuEnd is { } end && end.Total > start.Total && end.Idle >= start.Idle)
            hostCpu = Math.Round(Math.Clamp(100d * (1d - (double)(end.Idle - start.Idle) / (end.Total - start.Total)), 0, 100), 1);
        var hostMemory = ReadHostMemory();
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
            DateTime.UtcNow,
            Math.Max(0, (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds),
            ReadHostUptime(), hostCpu, hostMemory?.Used, hostMemory?.Total));
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
            var latestRequest = group.First();
            if (string.Equals(latestRequest.Component, "Backend API", StringComparison.OrdinalIgnoreCase))
                continue;
            var confirmed = group.FirstOrDefault(x => x.Status is "Succeeded" or "Deployed");
            rows.Add(confirmed is null
                ? new SystemVersionDto(latestRequest.Component, "unknown", "Unverified", "request-history")
                : new SystemVersionDto(confirmed.Component, confirmed.Version, "Recorded", "deployment-history"));
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
            Status = "DeployRequested",
            DeployedBy = actor,
            DeployedByUserId = userId,
            DeployedAt = DateTime.UtcNow,
        };
        _db.SystemDeployments.Add(row);
        await AuditAsync(userId, "Deploy", "SystemDeployment", row.Id, $"{row.Component} {row.Version}", null, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SystemDeploymentDto>.Ok(
            MapDeployment(row), "Yêu cầu deploy đã được ghi nhận; chưa có worker xác nhận hoàn tất.");
    }

    public async Task<ApiResponse<SystemDeploymentDto>> RollbackAsync(
        RollbackRequest request, Guid? userId, string? actor, CancellationToken ct = default)
    {
        var history = await _db.SystemDeployments
            .Where(x => x.Component == request.Component && x.Environment == request.Environment
                && (x.Status == "Succeeded" || x.Status == "Deployed"))
            .OrderByDescending(x => x.DeployedAt)
            .ToListAsync(ct);
        history = history.DistinctBy(x => x.Version).Take(2).ToList();
        if (history.Count < 2)
            throw AppException.Conflict("Không có hai phiên bản triển khai thành công để rollback; yêu cầu chưa thực thi không phải bản đã deploy.");

        var previous = history[1];
        var row = new SystemDeployment
        {
            Component = previous.Component,
            Version = previous.Version,
            Environment = previous.Environment,
            Status = "RollbackRequested",
            DeployedBy = actor,
            DeployedByUserId = userId,
            DeployedAt = DateTime.UtcNow,
        };
        _db.SystemDeployments.Add(row);
        await AuditAsync(userId, "Rollback", "SystemDeployment", row.Id, $"{row.Component} {row.Version}", null, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<SystemDeploymentDto>.Ok(
            MapDeployment(row), "Yêu cầu rollback đã được ghi nhận; chưa có worker xác nhận hoàn tất.");
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
        _ = await RequireDevice(deviceId, ct);
        // Protocol 2 defines relay desired-state commands, not safe device reboot.
        throw new AppException(
            "Restart thiết bị chưa được hỗ trợ an toàn bởi Edge Protocol 2; không xếp lệnh restart.",
            501);
    }

    public async Task<ApiResponse<DeviceActionResultDto>> UpdateFirmwareAsync(
        Guid deviceId, UpdateFirmwareRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FirmwareVersion))
            throw AppException.BadRequest("Firmware version là bắt buộc.");
        var device = await RequireDevice(deviceId, ct);
        var requestedVersion = request.FirmwareVersion.Trim();
        await AuditAsync(userId, "FirmwareUpdateRequested", "Device", device.Id, requestedVersion, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<DeviceActionResultDto>.Ok(
            new DeviceActionResultDto(device.Id, "Requested", requestedVersion),
            "Yêu cầu firmware đã được ghi nhận; FirmwareVersion chỉ đổi sau ACK từ thiết bị.");
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
            device.Id, device.DeviceCode, CameraConfiguration.IsCamera(device) ? CameraConfiguration.Status(device, DateTime.UtcNow) : device.Status.ToString(), device.LastSeenAt,
            device.BatteryLevel, device.RssiDbm, device.IpAddress,
            CameraConfiguration.IsCamera(device) && device.FirmwareVersion?.Contains("://") == true ? null : device.FirmwareVersion,
            sensorIds.Count, pending, latest, recent));
    }

    public async Task<ApiResponse<DeviceConnectionTestDto>> TestDeviceConnectionAsync(
        Guid deviceId, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var device = await RequireDevice(deviceId, ct);
        if (CameraConfiguration.IsCamera(device))
        {
            await AuditAsync(userId, "TestConnection", "Device", device.Id, "Camera Agent not integrated", ip, ct);
            await _db.SaveChangesAsync(ct);
            return ApiResponse<DeviceConnectionTestDto>.Ok(new DeviceConnectionTestDto(
                device.Id, false, "Camera Agent chưa tích hợp; chưa xác minh được kết nối/stream Camera.",
                device.IpAddress, null, device.LastSeenAt, device.RssiDbm, "NotIntegrated"));
        }
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
        var deviceSource = deviceId.ToString("N");
        var alerts = await _db.Alerts.AsNoTracking()
            .Where(a => (a.SensorId != null && sensorIds.Contains(a.SensorId.Value) && a.IncidentType == "sensor_disconnect")
                || (a.IncidentType == "device_disconnect" && a.SourceType == "Device" && a.SourceId == deviceSource))
            .OrderByDescending(a => a.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var rows = logs.Select(x => new SystemLogDto(x.Id, x.CreatedAt, "INFO", "device", x.Action, null, device.DeviceCode, x.IpAddress))
            .Concat(alerts.Select(a => new SystemLogDto(a.Id, a.CreatedAt, a.Severity.ToString(), "alert", a.Message, null, device.DeviceCode, null)))
            .OrderByDescending(x => x.At)
            .ToList();
        return ApiResponse<IReadOnlyList<SystemLogDto>>.Ok(rows);
    }

    public async Task<ApiResponse<IReadOnlyList<CameraRecordingDto>>> GetRecordingSettingsAsync(CancellationToken ct = default)
    {
        var devices = await CameraDevices(ct);
        var settings = await _db.CameraRecordingSettings.AsNoTracking().ToListAsync(ct);
        var areas = await _db.FarmingAreas.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var rows = devices.Select(device =>
        {
            var setting = settings.FirstOrDefault(x => x.DeviceId == device.Id);
            return MapCamera(device, setting, device.FarmingAreaId is Guid areaId ? areas.GetValueOrDefault(areaId) : null);
        }).ToList();
        return ApiResponse<IReadOnlyList<CameraRecordingDto>>.Ok(rows);
    }

    public async Task<ApiResponse<CameraRecordingDto>> UpsertRecordingAsync(
        Guid deviceId, UpsertCameraRecordingRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (!RecordingModes.Contains(request.Mode))
            throw AppException.BadRequest("Mode không hợp lệ.");
        if (request.RetentionDays is < 1 or > 3650 || request.Fps is < 1 or > 120 || request.MaxStorageGb is < 1)
            throw AppException.BadRequest("RetentionDays, FPS hoặc giới hạn dung lượng không hợp lệ.");
        CameraConfiguration.ValidateSchedule(request.WeeklyScheduleJson, request.Mode == "Scheduled");
        var device = await RequireDevice(deviceId, ct);
        if (!IsCameraDevice(device)) throw AppException.BadRequest("Thiết bị không phải camera.");
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
        var areaName = device.FarmingAreaId is Guid areaId
            ? await _db.FarmingAreas.Where(x => x.Id == areaId).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return ApiResponse<CameraRecordingDto>.Ok(MapCamera(device, setting, areaName),
            "Đã lưu cấu hình; worker ghi hình chưa được tích hợp, chưa xác nhận đang ghi.");
    }

    public async Task<ApiResponse<IReadOnlyList<RecordingHistoryDto>>> GetRecordingHistoryAsync(Guid? deviceId, CancellationToken ct = default)
    {
        var query = CameraRecordingLibrary.TechnicalVideos(_db);
        if (deviceId.HasValue)
        {
            if (!await CameraRecordingLibrary.CameraDevices(_db).AnyAsync(x => x.Id == deviceId.Value, ct))
                throw AppException.NotFound("Camera");
            query = query.Where(x => x.DeviceId == deviceId);
        }
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
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ModelName)
            || string.IsNullOrWhiteSpace(request.Version))
            throw AppException.BadRequest("Name, ModelName và Version là bắt buộc.");
        if (request.ConfidenceThreshold is < 0 or > 1 || request.FrameSamplingFps is <= 0 or > 120
            || request.BatchSize is < 1 or > 128)
            throw AppException.BadRequest("Confidence, FPS hoặc BatchSize không hợp lệ.");
        if (!string.IsNullOrWhiteSpace(request.DetectionClassesJson))
        {
            try
            {
                using var labels = System.Text.Json.JsonDocument.Parse(request.DetectionClassesJson);
                if (labels.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array
                    || labels.RootElement.EnumerateArray().Any(x => x.ValueKind != System.Text.Json.JsonValueKind.String))
                    throw AppException.BadRequest("DetectionClassesJson phải là mảng chuỗi.");
            }
            catch (System.Text.Json.JsonException) { throw AppException.BadRequest("DetectionClassesJson không hợp lệ."); }
        }
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
        row.Status = "RestartRequested";
        await AuditAsync(userId, "RestartAiService", "AiManagedService", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(
            MapAi(row, 0), "Yêu cầu restart đã được ghi nhận; chưa có worker xác nhận.");
    }

    public async Task<ApiResponse<AiServiceDto>> StopAiServiceAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await RequireAi(id, ct);
        row.Status = "StopRequested";
        await AuditAsync(userId, "StopAiService", "AiManagedService", row.Id, row.Name, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(
            MapAi(row, 0), "Yêu cầu stop đã được ghi nhận; chưa có worker xác nhận.");
    }

    public async Task<ApiResponse<AiServiceDto>> DeployAiModelAsync(
        Guid id, DeployModelRequest request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
            throw AppException.BadRequest("Version là bắt buộc.");
        var row = await RequireAi(id, ct);
        var requestedVersion = request.Version.Trim();
        row.Status = "DeployRequested";
        await AuditAsync(userId, "DeployAiModelRequested", "AiManagedService", row.Id, requestedVersion, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(
            MapAi(row, 0), $"Đã ghi nhận yêu cầu deploy model {requestedVersion}; version hiện tại chưa đổi.");
    }

    public async Task<ApiResponse<AiServiceDto>> RollbackAiModelAsync(Guid id, Guid? userId, string? ip, CancellationToken ct = default)
    {
        var row = await RequireAi(id, ct);
        if (string.IsNullOrWhiteSpace(row.PreviousVersion))
            throw AppException.BadRequest("Không có version trước.");
        row.Status = "RollbackRequested";
        await AuditAsync(userId, "RollbackAiModelRequested", "AiManagedService", row.Id, row.PreviousVersion, ip, ct);
        await _db.SaveChangesAsync(ct);
        return ApiResponse<AiServiceDto>.Ok(
            MapAi(row, 0), $"Đã ghi nhận yêu cầu rollback về {row.PreviousVersion}; version hiện tại chưa đổi.");
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
        var row = await _db.BackupScheduleSettings.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        return ApiResponse<BackupScheduleDto>.Ok(row is null
            ? new BackupScheduleDto(false, "Daily", "02:00", 30)
            : new BackupScheduleDto(row.Enabled, row.Frequency, row.BackupTime, row.RetentionDays,
                row.LastAttemptSlotUtc, row.LastCompletedAtUtc, row.LastResult, row.LastBackupFile));
    }

    public async Task<ApiResponse<BackupScheduleDto>> UpdateBackupScheduleAsync(
        BackupScheduleDto request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (request.RetentionDays is < 1 or > 3650)
            throw AppException.BadRequest("RetentionDays phải trong khoảng 1–3650.");
        if (!TimeOnly.TryParseExact(request.BackupTime, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
            throw AppException.BadRequest("BackupTime phải có định dạng HH:mm.");
        if (request.Frequency is not ("Daily" or "Weekly"))
            throw AppException.BadRequest("Frequency phải là Daily hoặc Weekly (thứ Hai).");
        var row = await _db.BackupScheduleSettings.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
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
        return ApiResponse<BackupScheduleDto>.Ok(new BackupScheduleDto(row.Enabled, row.Frequency, row.BackupTime, row.RetentionDays,
            row.LastAttemptSlotUtc, row.LastCompletedAtUtc, row.LastResult, row.LastBackupFile));
    }

    public async Task<ApiResponse<SecurityOverviewDto>> GetSecurityOverviewAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var failed = await _db.OperationLogs.CountAsync(
            x => x.Action == "LoginFailed" && x.CreatedAt >= since, ct);
        var sessions = await _db.AppUsers.CountAsync(x => x.RefreshTokenExpiry > DateTime.UtcNow && x.IsActive, ct);
        var keys = await _db.ServiceApiKeys.CountAsync(x => x.RevokedAt == null, ct);
        var alerts = await _db.OperationLogs.CountAsync(
            x => x.Action == "LoginFailureThresholdReached" && x.CreatedAt >= since, ct);
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
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiry = null;
        user.RefreshTokenVersion++;
        user.TokenVersion++;
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
            : new SecurityPolicyDto(Math.Clamp(row.SessionDays, 1, 90), Math.Clamp(row.FailedLoginAlertThreshold, 1, 20), true));
    }

    public async Task<ApiResponse<SecurityPolicyDto>> UpdatePolicyAsync(
        SecurityPolicyDto request, Guid? userId, string? ip, CancellationToken ct = default)
    {
        if (request.SessionDays is < 1 or > 90)
            throw AppException.BadRequest("SessionDays phải từ 1 đến 90.");
        if (request.FailedLoginAlertThreshold is < 1 or > 20)
            throw AppException.BadRequest("FailedLoginAlertThreshold phải từ 1 đến 20.");
        if (!request.RequireConfirmRestore)
            throw AppException.BadRequest("Không thể tắt xác nhận bắt buộc khi phục hồi database.");
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

    // Explicit technical incident allowlist. Do not infer authorization from free-text messages.
    private IQueryable<Alert> TechnicalAlerts() => _db.Alerts.AsNoTracking().Where(a =>
        (a.IncidentType == "sensor_disconnect" && a.SourceType == "Sensor") ||
        (a.IncidentType == "device_disconnect" && a.SourceType == "Device") ||
        (a.IncidentType == "kiosk_disconnect" && a.SourceType == "Kiosk"));

    private static readonly string[] TechnicalActions =
    [
        "Deploy", "Rollback", "RestartRequested", "FirmwareUpdateRequested", "DisableDevice",
        "TestConnection", "UpdateRecording", "CreateAiService", "RestartAiService", "StopAiService",
        "DeployAiModelRequested", "RollbackAiModelRequested", "UpdateBackupSchedule",
        "TerminateSession", "CreateApiKey", "RotateApiKey", "RevokeApiKey", "UpdateSecurityPolicy",
        "DeleteCameraRecordingRequested", "DeleteCameraRecordingFailed", "DeleteCameraRecording",
        "Login", "LoginFailed", "LoginFailureThresholdReached"
    ];

    private IQueryable<OperationLog> TechnicalOperations() => _db.OperationLogs.AsNoTracking()
        .Where(x => TechnicalActions.Contains(x.Action));

    public async Task<ApiResponse<IReadOnlyList<SystemTechnicalAlertDto>>> GetTechnicalAlertsAsync(
        bool activeOnly, CancellationToken ct = default)
    {
        var query = TechnicalAlerts();
        if (activeOnly) query = query.Where(a => a.Status == AlertStatus.Active ||
            a.Status == AlertStatus.Acknowledged || a.Status == AlertStatus.InProgress);
        var rows = await query.OrderByDescending(a => a.LastOccurredAt ?? a.CreatedAt)
            .Take(200).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SystemTechnicalAlertDto>>.Ok(rows.Select(a =>
            new SystemTechnicalAlertDto(a.Id, a.Message, a.Severity.ToString(), a.Status.ToString(),
                a.CreatedAt, "Device", a.IncidentType, a.LastOccurredAt)).ToList());
    }

    public async Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetLogsAsync(
        string? source, string? severity, string? search, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var logs = new List<SystemLogDto>();
        if (source is null or "" or "system" or "audit" or "security" or "ai")
        {
            var query = TechnicalOperations();
            if (source == "security") query = query.Where(x => x.Action == "Login" || x.Action == "LoginFailed" ||
                x.Action == "LoginFailureThresholdReached" || x.EntityType == "ServiceApiKey" ||
                x.EntityType == "SecurityPolicy" || x.Action == "TerminateSession");
            else if (source == "ai") query = query.Where(x => x.EntityType == "AiManagedService");
            if (from.HasValue) query = query.Where(x => x.CreatedAt >= from);
            if (to.HasValue) query = query.Where(x => x.CreatedAt <= to);
            var rows = await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
            logs.AddRange(rows.Select(x => new SystemLogDto(x.Id, x.CreatedAt,
                x.Action == "LoginFailed" || x.Action == "LoginFailureThresholdReached" ? "WARNING" : "INFO",
                x.Action == "Login" || x.Action == "LoginFailed" || x.Action == "LoginFailureThresholdReached" ||
                    x.EntityType == "ServiceApiKey" || x.EntityType == "SecurityPolicy" || x.Action == "TerminateSession"
                    ? "security" : x.EntityType == "AiManagedService" ? "ai" : "system",
                x.Action, x.UserId.ToString(), x.EntityType, x.IpAddress)));
        }
        if (source is null or "" or "device" or "alert")
        {
            var query = TechnicalAlerts();
            if (from.HasValue) query = query.Where(x => x.CreatedAt >= from);
            if (to.HasValue) query = query.Where(x => x.CreatedAt <= to);
            var rows = await query.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
            logs.AddRange(rows.Select(a => new SystemLogDto(a.Id, a.CreatedAt, a.Severity.ToString(),
                "alert", a.Message, null, a.SourceId, null)));
        }
        IEnumerable<SystemLogDto> filtered = logs;
        if (!string.IsNullOrWhiteSpace(severity))
            filtered = filtered.Where(x => string.Equals(x.Severity, severity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(x => x.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (x.Actor?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        return ApiResponse<IReadOnlyList<SystemLogDto>>.Ok(filtered.OrderByDescending(x => x.At).Take(400).ToList());
    }

    public async Task<ApiResponse<IReadOnlyList<SystemLogDto>>> GetAuditLogsAsync(CancellationToken ct = default)
    {
        var rows = await TechnicalOperations().OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
        var users = await _db.AppUsers.AsNoTracking().Select(x => new { x.Id, x.Username }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<SystemLogDto>>.Ok(rows.Select(x => new SystemLogDto(
            x.Id, x.CreatedAt, "INFO", "audit", x.Action,
            users.FirstOrDefault(u => u.Id == x.UserId)?.Username, x.EntityType, x.IpAddress)).ToList());
    }

    private async Task<Device> RequireDevice(Guid id, CancellationToken ct)
        => await _db.Devices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Device");

    private async Task<AiManagedService> RequireAi(Guid id, CancellationToken ct)
        => await _db.AiManagedServices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("AI service");

    private async Task<List<Device>> CameraDevices(CancellationToken ct)
    {
        var devices = await _db.Devices.AsNoTracking().ToListAsync(ct);
        return devices.Where(IsCameraDevice).ToList();
    }

    private static bool IsCameraDevice(Device device) =>
        (device.DeviceType ?? "").Contains("cam", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrWhiteSpace(device.StreamUrl)
        || !string.IsNullOrWhiteSpace(device.SnapshotUrl);

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

    private static CameraRecordingDto MapCamera(Device device, CameraRecordingSetting? setting, string? areaName) => new(
        setting?.Id, device.Id, device.DeviceCode, device.Name, areaName,
        CameraConfiguration.Status(device, DateTime.UtcNow), device.Resolution,
        CameraConfiguration.SafeUrl(device.SnapshotUrl, snapshot: true), CameraConfiguration.SafeUrl(device.StreamUrl),
        setting?.Mode ?? "Disabled", setting?.RetentionDays ?? 30, setting?.VideoQuality,
        setting?.Fps, setting?.MaxStorageGb, setting?.WeeklyScheduleJson,
        setting is null || setting.Mode == "Disabled" ? "Disabled" : "NotIntegrated");

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

    // Linux host metrics; unavailable on unsupported hosts rather than returning fake zeroes.
    private static (long Total, long Idle)? ReadHostCpu()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var line = File.ReadLines("/proc/stat").First();
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 9 || fields[0] != "cpu") return null;
            var values = fields.Skip(1).Take(8).Select(v => long.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            return (values.Sum(), values[3] + values[4]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException or InvalidOperationException) { return null; }
    }

    private static (long Used, long Total)? ReadHostMemory()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            long? total = null, available = null;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2) continue;
                if (fields[0] == "MemTotal:") total = checked(long.Parse(fields[1], CultureInfo.InvariantCulture) * 1024);
                if (fields[0] == "MemAvailable:") available = checked(long.Parse(fields[1], CultureInfo.InvariantCulture) * 1024);
            }
            return total is > 0 && available is >= 0 && available <= total
                ? (total.Value - available.Value, total.Value) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException) { return null; }
    }

    private static long? ReadHostUptime()
    {
        if (!OperatingSystem.IsLinux()) return null;
        try
        {
            var field = File.ReadAllText("/proc/uptime").Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && double.IsFinite(seconds) && seconds >= 0 && seconds < long.MaxValue ? (long)seconds : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException) { return null; }
    }

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
