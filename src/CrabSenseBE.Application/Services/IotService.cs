using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// IoT: ESP32 gateway (Device) → Sensors → HTTP ingest → kiosk live view.
/// </summary>
public class IotService : IIotService
{
    private readonly IUnitOfWork _uow;
    private readonly IStorageService _storage;
    private readonly IAlertService _alerts;
    private readonly IRasFlowService _rasFlow;
    private readonly ILogger<IotService> _logger;
    private readonly FarmAreaScope _areas;
    private readonly IConfiguration? _configuration;

    public IotService(
        IUnitOfWork uow,
        IStorageService storage,
        IAlertService alerts,
        IRasFlowService rasFlow,
        ILogger<IotService> logger,
        ICurrentUserService? currentUser = null,
        IConfiguration? configuration = null)
    {
        _uow = uow;
        _storage = storage;
        _alerts = alerts;
        _rasFlow = rasFlow;
        _logger = logger;
        _areas = new FarmAreaScope(uow, currentUser);
        _configuration = configuration;
    }

    // ─── Ingest (ESP32 HTTP) ────────────────────────────────────────────────

    public async Task<ApiResponse> IngestSensorDataAsync(
        SensorDataRequest req,
        Guid authenticatedDeviceId,
        string authenticatedDeviceCode,
        CancellationToken ct = default)
    {
        var (device, sensor) = await RequireIngestOwnershipAsync(
            req, authenticatedDeviceId, authenticatedDeviceCode, ct);

        var eventKey = string.IsNullOrWhiteSpace(req.EventId)
            ? null
            : $"edge:{req.EventId.Trim()}:{req.SensorCode.Trim()}";
        var edgeEventKey = eventKey is null ? null : Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(req.EventId!.Trim())));
        if (eventKey is not null
            && await _uow.WaterMeasurements.AnyAsync(m => m.SensorId == sensor.Id && (m.EdgeEventKey == edgeEventKey || m.Source == eventKey), ct))
            return ApiResponse.Ok("Sensor data already ingested.");

        device.LastSeenAt = DateTime.UtcNow;
        device.Status = DeviceStatus.Online;
        if (!string.IsNullOrWhiteSpace(req.IpAddress))
            device.IpAddress = req.IpAddress.Trim();

        var measurement = new WaterMeasurement
        {
            SensorId = sensor.Id,
            WaterSystemId = sensor.WaterSystemId,
            Value = req.Value,
            Unit = req.Unit ?? sensor.Unit,
            MeasuredAt = req.MeasuredAt == default ? DateTime.UtcNow : req.MeasuredAt,
            Source = eventKey ?? "realtime",
            EdgeEventKey = edgeEventKey
        };
        await _uow.WaterMeasurements.AddAsync(measurement, ct);

        sensor.LastSeenAt = DateTime.UtcNow;
        try { await _uow.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (edgeEventKey is not null && ex.Entries.Any(e => e.Entity is WaterMeasurement))
        {
            // Concurrent duplicate is protected by the unique index; retry in a fresh request.
            throw AppException.Conflict("Ghi dữ liệu sensor xung đột; vui lòng gửi lại cùng EventId.");
        }

        try
        {
            await _alerts.EvaluateMeasurementAsync(sensor, req.Value, ct);
            if (sensor.SensorCode.StartsWith("float_", StringComparison.Ordinal))
                await EvaluateTanksAsync(
                    sensor, req.Value, measurement.MeasuredAt, ct);
            if (sensor.DeviceId is Guid deviceId)
                await _rasFlow.ApplyAutoRelaysAsync(deviceId, ct);
        }
        catch (Exception ex) when (ex is not AppException)
        {
            _logger.LogWarning(ex, "Ingest saved but follow-up failed for {SensorCode}", sensor.SensorCode);
        }
        return ApiResponse.Ok("Sensor data ingested.");
    }

    public async Task ReportRelayAsync(string deviceCode, int channel, bool on, Guid? actorId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceCode) || channel < 1)
            throw AppException.BadRequest("DeviceCode và channel hợp lệ là bắt buộc.");
        var device = await _uow.Devices.FirstOrDefaultAsync(
            d => d.DeviceCode == deviceCode.Trim(), ct);
        if (device is null) throw AppException.NotFound("Device");
        if (device.EdgeState is not ("Approved" or "Online"))
            throw AppException.Forbidden("Controller chưa được phê duyệt.");
        var channelText = channel.ToString();
        var nodes = await _uow.RasComponents.FindAsync(
            c => c.RelayDeviceId == device.Id && c.RelayChannel == channelText, ct);
        if (!nodes.Any())
            throw AppException.BadRequest("Channel không thuộc controller này.");
        var logged = false;
        foreach (var node in nodes)
        {
            var hadObservation = node.LastReportedAt.HasValue;
            var wasOn = node.IsOn;
            node.IsOn = on;
            node.RunStartedAt = on
                ? hadObservation && wasOn
                    ? node.RunStartedAt
                    : DateTime.UtcNow
                : null;
            node.LastReportedAt = DateTime.UtcNow;
            // This tracked entity only changes observed fields, not control mode.
            var offMessage = $"{node.Name} đã tắt";
            var onMessage = $"{node.Name} đã bật";
            if (hadObservation && wasOn && !on)
            {
                await _alerts.ClearAsync(
                    "relay_on", "RasComponent", node.Id.ToString("N"), ct);
                await _alerts.RaiseOnceAsync(
                    "relay_off", "RasComponent", node.Id.ToString("N"),
                    offMessage, NotificationTypeCodes.RasRelayOff,
                    await AreaOfWaterSystemAsync(node.WaterSystemId, ct),
                    new Dictionary<string, string?>
                    {
                        ["name"] = node.Name,
                        ["code"] = node.Code,
                        ["device"] = device.DeviceCode
                    }, ct);
            }
            else if (hadObservation && !wasOn && on)
            {
                await _alerts.ClearAsync(
                    "relay_off", "RasComponent", node.Id.ToString("N"), ct);
                await _alerts.RaiseOnceAsync(
                    "relay_on", "RasComponent", node.Id.ToString("N"),
                    onMessage, NotificationTypeCodes.RasRelayOn,
                    await AreaOfWaterSystemAsync(node.WaterSystemId, ct),
                    new Dictionary<string, string?>
                    {
                        ["name"] = node.Name,
                        ["code"] = node.Code,
                        ["device"] = device.DeviceCode
                    }, ct);
            }
            logged |= await LogRelayAsync(actorId, node.Id, "RasComponent", node.WaterSystemId, node.Name, device.DeviceCode, channel, on, ct);
        }
        if (!logged)
            await LogRelayAsync(actorId, device.Id, "Device", null, $"kênh {channel}", device.DeviceCode, channel, on, ct);
        await _uow.SaveChangesAsync(ct);
    }

    private async Task WriteDeviceAuditAsync(
        Guid? actorId, string action, Guid entityId, string? details, string? ip, CancellationToken ct)
    {
        if (actorId is not Guid uid || uid == Guid.Empty) return;
        await _uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = uid,
            Action = action,
            EntityType = "Device",
            EntityId = entityId,
            Details = details,
            IpAddress = ip
        }, ct);
    }

    private async Task<bool> LogRelayAsync(
        Guid? actorId, Guid entityId, string entityType, Guid? waterSystemId,
        string name, string deviceCode, int channel, bool on, CancellationToken ct)
    {
        if (actorId is not Guid uid || uid == Guid.Empty) return false;
        var areaKey = "";
        if (waterSystemId is Guid wsId)
        {
            var ws = await _uow.WaterSystems.GetByIdAsync(wsId, ct);
            if (ws?.FarmingAreaId is Guid areaId && areaId != Guid.Empty)
                areaKey = areaId.ToString("N");
        }
        await _uow.OperationLogs.AddAsync(new OperationLog
        {
            UserId = uid,
            Action = on ? "ras_on" : "ras_off",
            EntityType = entityType,
            EntityId = entityId,
            Details = $"{areaKey}|{(on ? "Bật" : "Tắt")} {name}|{deviceCode}|ch{channel}"
        }, ct);
        return true;
    }

    private async Task EvaluateTanksAsync(
        Sensor sourceSensor,
        decimal value,
        DateTime measuredAt,
        CancellationToken ct)
    {
        if (!sourceSensor.IsActive
            || sourceSensor.DeviceId is not Guid deviceId
            || sourceSensor.WaterSystemId is not Guid waterSystemId)
            return;
        var now = DateTime.UtcNow;
        var nodes = await _uow.RasComponents.FindAsync(
            c => c.WaterSystemId == waterSystemId
                && c.ParamDefaultsJson != null, ct);
        foreach (var node in nodes)
        {
            System.Text.Json.JsonDocument doc;
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(node.ParamDefaultsJson!);
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            using (doc)
            {
            var root = doc.RootElement;
            string text(string key) =>
                root.TryGetProperty(key, out var item) ? item.ToString() : "";
            var group = text("group");
            if (group is "electric" or "pump") continue;
            var low = text("lowFloat");
            var high = text("highFloat");
            if (low != sourceSensor.SensorCode
                && high != sourceSensor.SensorCode) continue;
            var name = text("label");
            if (string.IsNullOrWhiteSpace(name)) name = node.Name;
            var lowVal = low == sourceSensor.SensorCode
                ? FreshIncoming(value, measuredAt, now)
                : await LatestFloatAsync(
                    low, deviceId, waterSystemId, now, ct);
            var highVal = high == sourceSensor.SensorCode
                ? FreshIncoming(value, measuredAt, now)
                : await LatestFloatAsync(
                    high, deviceId, waterSystemId, now, ct);
            var level = TankLevel(low, text("lowWhen"), lowVal, high, text("highWhen"), highVal);
            var areaId = await AreaOfWaterSystemAsync(node.WaterSystemId, ct);
            var tokens = new Dictionary<string, string?> { ["name"] = name, ["code"] = node.Code };
            if (level == "Không an toàn")
            {
                await _alerts.RaiseOnceAsync(
                    "tank_sensor_conflict", "RasComponent",
                    node.Id.ToString("N"),
                    $"{name} có tín hiệu phao mâu thuẫn",
                    NotificationTypeCodes.TankOverflow,
                    areaId, tokens, ct);
            }
            else if (level == "Tràn")
            {
                await _alerts.ClearAsync(
                    "tank_sensor_conflict", "RasComponent",
                    node.Id.ToString("N"), ct);
                await _alerts.ClearAsync(
                    "tank_empty", "RasComponent", node.Id.ToString("N"), ct);
                await _alerts.RaiseOnceAsync(
                    "tank_overflow", "RasComponent", node.Id.ToString("N"),
                    $"{name} đang tràn", NotificationTypeCodes.TankOverflow,
                    areaId, tokens, ct);
            }
            else if (level == "Cạn")
            {
                await _alerts.ClearAsync(
                    "tank_sensor_conflict", "RasComponent",
                    node.Id.ToString("N"), ct);
                await _alerts.ClearAsync(
                    "tank_overflow", "RasComponent", node.Id.ToString("N"), ct);
                await _alerts.RaiseOnceAsync(
                    "tank_empty", "RasComponent", node.Id.ToString("N"),
                    $"{name} đang cạn", NotificationTypeCodes.TankEmpty,
                    areaId, tokens, ct);
            }
            else if (level == "Bình thường")
            {
                await _alerts.ClearAsync(
                    "tank_sensor_conflict", "RasComponent",
                    node.Id.ToString("N"), ct);
                await _alerts.ClearAsync(
                    "tank_overflow", "RasComponent", node.Id.ToString("N"), ct);
                await _alerts.ClearAsync(
                    "tank_empty", "RasComponent", node.Id.ToString("N"), ct);
            }
            }
        }
    }

    private async Task<Guid?> AreaOfWaterSystemAsync(Guid? waterSystemId, CancellationToken ct)
    {
        if (waterSystemId is not Guid id || id == Guid.Empty) return null;
        return (await _uow.WaterSystems.GetByIdAsync(id, ct))?.FarmingAreaId;
    }

    private decimal? FreshIncoming(
        decimal value,
        DateTime measuredAt,
        DateTime now) =>
        IsFreshMeasurement(measuredAt, now) ? value : null;

    private async Task<decimal?> LatestFloatAsync(
        string code,
        Guid deviceId,
        Guid waterSystemId,
        DateTime now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var sensor = await _uow.Sensors.FirstOrDefaultAsync(
            s => s.SensorCode == code
                && s.DeviceId == deviceId
                && s.WaterSystemId == waterSystemId
                && s.IsActive, ct);
        if (sensor is null) return null;
        var latest = (await _uow.WaterMeasurements.FindAsync(m => m.SensorId == sensor.Id, ct))
            .OrderByDescending(m => m.MeasuredAt)
            .FirstOrDefault();
        return latest is not null
            && sensor.LastSeenAt is DateTime lastSeenAt
            && IsFreshMeasurement(lastSeenAt, now)
            && IsFreshMeasurement(latest.MeasuredAt, now)
            ? latest.Value
            : null;
    }

    private bool IsFreshMeasurement(DateTime measuredAt, DateTime now)
    {
        var maxAgeSeconds = Math.Clamp(
            _configuration?.GetValue(
                "RasAutomation:SensorFreshnessSeconds:Float",
                _configuration?.GetValue(
                    "RasAutomation:DefaultSensorFreshnessSeconds", 30) ?? 30)
                ?? 30,
            5,
            3600);
        var futureToleranceSeconds = Math.Clamp(
            _configuration?.GetValue(
                "RasAutomation:FutureTimestampToleranceSeconds", 5) ?? 5,
            0,
            60);
        return measuredAt >= now.AddSeconds(-maxAgeSeconds)
            && measuredAt <= now.AddSeconds(futureToleranceSeconds);
    }

    /// <summary>Cạn and tràn come from the two assigned floats. Bình thường is neither.</summary>
    private static string? TankLevel(
        string low, string lowWhen, decimal? lowVal,
        string high, string highWhen, decimal? highVal)
    {
        bool assigned(string sensor) => !string.IsNullOrWhiteSpace(sensor);
        if (!assigned(low) && !assigned(high)) return null;
        bool hit(string sensor, string when, decimal? reading)
        {
            if (!assigned(sensor) || reading is null) return false;
            var isOn = reading >= 0.5m;
            var wantOn = when != "off";
            return isOn == wantOn;
        }
        var highHit = hit(high, highWhen, highVal);
        var lowHit = hit(low, lowWhen, lowVal);
        if (highHit && lowHit) return "Không an toàn";
        if (highHit) return "Tràn";
        if (lowHit) return "Cạn";
        if ((assigned(low) && lowVal is null) || (assigned(high) && highVal is null)) return null;
        return "Bình thường";
    }

    public async Task<ApiResponse<IReadOnlyList<SensorDataBatchItemResult>>> IngestSensorDataBatchAsync(
        SensorDataBatchRequest req,
        Guid authenticatedDeviceId,
        string authenticatedDeviceCode,
        CancellationToken ct = default)
    {
        var list = req.Measurements?.ToList() ?? new List<SensorDataRequest>();
        if (list.Count == 0)
            throw AppException.BadRequest("Measurements are required.");

        // Validate the complete ownership set before the first SaveChanges so a
        // foreign sensor cannot leave an earlier item partially persisted.
        foreach (var item in list)
            _ = await RequireIngestOwnershipAsync(
                item, authenticatedDeviceId, authenticatedDeviceCode, ct);

        var results = new List<SensorDataBatchItemResult>(list.Count);
        for (var index = 0; index < list.Count; index++)
        {
            var item = list[index];
            try
            {
                var result = await IngestSensorDataAsync(
                    item, authenticatedDeviceId, authenticatedDeviceCode, ct);
                results.Add(new(item.SensorCode, item.EventId, true, result.Message ?? "Saved."));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (ex is not AppException) _logger.LogError(ex, "Sensor batch item failed");
                results.Add(new(item.SensorCode, item.EventId, false,
                    ex is AppException ? ex.Message : "Sensor data could not be saved."));
                foreach (var skipped in list.Skip(index + 1))
                    results.Add(new(
                        skipped.SensorCode, skipped.EventId, false,
                        "Not processed because an earlier batch item failed."));
                break;
            }
        }
        var succeeded = results.Count(result => result.Success);
        return ApiResponse<IReadOnlyList<SensorDataBatchItemResult>>.Ok(
            results, $"Batch processed: {succeeded} succeeded, {results.Count - succeeded} failed.");
    }

    private async Task<(Device Device, Sensor Sensor)> RequireIngestOwnershipAsync(
        SensorDataRequest req,
        Guid authenticatedDeviceId,
        string authenticatedDeviceCode,
        CancellationToken ct)
    {
        if (authenticatedDeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(authenticatedDeviceCode))
            throw AppException.Unauthorized("Device identity is required.");
        if (string.IsNullOrWhiteSpace(req.DeviceCode)
            || !string.Equals(
                req.DeviceCode.Trim(),
                authenticatedDeviceCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden(
                "DeviceCode trong payload không khớp credential đã xác thực.");
        if (string.IsNullOrWhiteSpace(req.SensorCode))
            throw AppException.BadRequest("SensorCode is required.");

        var device = await _uow.Devices.GetByIdAsync(authenticatedDeviceId, ct)
            ?? throw AppException.Unauthorized("Authenticated device no longer exists.");
        if (!string.Equals(
                device.DeviceCode,
                authenticatedDeviceCode,
                StringComparison.OrdinalIgnoreCase))
            throw AppException.Unauthorized("Device identity is inconsistent.");

        var sensorCode = req.SensorCode.Trim();
        var sensor = await _uow.Sensors.FirstOrDefaultAsync(
            item => item.SensorCode == sensorCode, ct)
            ?? throw AppException.NotFound(
                $"Sensor '{sensorCode}' — register it through the technical configuration API.");
        if (sensor.DeviceId != authenticatedDeviceId)
            throw AppException.Forbidden(
                "Sensor không thuộc thiết bị đã xác thực.");
        return (device, sensor);
    }

    public async Task<ApiResponse<PagedResult<SensorDataDto>>> GetSensorDataAsync(
        Guid sensorId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
        await EnsureSensorReadAsync(sensorId, ct);
        var all = await _uow.WaterMeasurements.FindAsync(
            m => m.SensorId == sensorId
                 && (from == null || m.MeasuredAt >= from)
                 && (to == null || m.MeasuredAt <= to), ct);

        var total = all.Count();
        var items = all.OrderByDescending(m => m.MeasuredAt)
            .Skip((page - 1) * Math.Max(1, pageSize)).Take(Math.Clamp(pageSize, 1, 2000))
            .Select(m => new SensorDataDto(m.Id, m.SensorId, m.Value, m.Unit, m.MeasuredAt, m.Source));

        return ApiResponse<PagedResult<SensorDataDto>>.Ok(new PagedResult<SensorDataDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<SensorDataDto?>> GetLatestSensorDataAsync(Guid sensorId, CancellationToken ct = default)
    {
        await EnsureSensorReadAsync(sensorId, ct);
        var latest = (await _uow.WaterMeasurements.FindAsync(m => m.SensorId == sensorId, ct))
            .OrderByDescending(m => m.MeasuredAt)
            .FirstOrDefault();
        if (latest is null)
            return ApiResponse<SensorDataDto?>.Ok(null, "No readings yet.");
        return ApiResponse<SensorDataDto?>.Ok(
            new SensorDataDto(latest.Id, latest.SensorId, latest.Value, latest.Unit, latest.MeasuredAt, latest.Source));
    }

    public async Task<ApiResponse<IEnumerable<SensorLiveDto>>> GetLiveSnapshotAsync(
        Guid? deviceId = null,
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        var sensors = (await _uow.Sensors.GetAllAsync(ct)).AsEnumerable();
        var selectedAreas = await TechnicalReadAreasAsync(farmingAreaId, ct);
        if (selectedAreas is not null)
        {
            var allowedSensorIds = await SensorIdsInAreasAsync(selectedAreas, ct);
            sensors = sensors.Where(s => allowedSensorIds.Contains(s.Id));
        }
        if (deviceId.HasValue)
            sensors = sensors.Where(s => s.DeviceId == deviceId.Value);

        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var devices = (await _uow.Devices.GetAllAsync(ct)).ToDictionary(d => d.Id);

        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var rowIds = rows.Values
                .Where(r => r.FarmingAreaId == areaId)
                .Select(r => r.Id)
                .ToHashSet();
            bool DeviceInArea(Guid? id) =>
                id is Guid did && devices.TryGetValue(did, out var d) && (
                    d.FarmingAreaId == areaId
                    || (d.FarmingRowId != null && rowIds.Contains(d.FarmingRowId.Value)));
            // Cảm biến thuộc khu: hệ nước khu, dãy của khu, hoặc thiết bị gắn khu/dãy.
            sensors = sensors.Where(s =>
                (s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                || (s.FarmingRowId != null && rowIds.Contains(s.FarmingRowId.Value))
                || DeviceInArea(s.DeviceId));
        }
        var components = (await _uow.RasComponents.GetAllAsync(ct)).ToDictionary(c => c.Id);

        // Only the newest reading per sensor. WaterMeasurements grows with every telemetry
        // tick, so loading the whole table here made /iot/live take seconds over WAN.
        var latestBySensor = new Dictionary<Guid, WaterMeasurement>();
        var sensorIds = sensors.Select(s => s.Id).ToList();
        if (sensorIds.Count > 0)
        {
            var latest = await _uow.WaterMeasurements.Query()
                .AsNoTracking()
                .Where(m => sensorIds.Contains(m.SensorId))
                .GroupBy(m => m.SensorId)
                .Select(g => g.OrderByDescending(x => x.MeasuredAt).First())
                .ToListAsync(ct);
            foreach (var m in latest) latestBySensor[m.SensorId] = m;
        }

        var list = new List<SensorLiveDto>();
        foreach (var s in sensors.OrderBy(s => s.SensorCode))
        {
            devices.TryGetValue(s.DeviceId ?? Guid.Empty, out var dev);
            latestBySensor.TryGetValue(s.Id, out var latest);
            components.TryGetValue(s.RasComponentId ?? Guid.Empty, out var loc);
            string? alarm = null;
            if (latest is not null)
            {
                if (s.MinThreshold.HasValue && latest.Value < s.MinThreshold.Value)
                    alarm = "below_min";
                else if (s.MaxThreshold.HasValue && latest.Value > s.MaxThreshold.Value)
                    alarm = "above_max";
            }
            var rowId = s.FarmingRowId ?? dev?.FarmingRowId;
            rows.TryGetValue(rowId ?? Guid.Empty, out var row);
            list.Add(new SensorLiveDto(
                s.Id, s.SensorCode, s.SensorType, s.Unit,
                s.MinThreshold, s.MaxThreshold, s.IsActive,
                s.DeviceId, dev?.DeviceCode, dev?.Status.ToString(),
                latest?.Value, latest?.MeasuredAt, s.LastSeenAt, alarm,
                loc?.Name, loc?.Type,
                rowId, row?.Name, row?.Code));
        }
        return ApiResponse<IEnumerable<SensorLiveDto>>.Ok(list);
    }

    public async Task<ApiResponse<IEnumerable<WaterQualitySnapshotDto>>> GetWaterQualityHistoryAsync(
        Guid? farmingAreaId = null,
        string period = "24h",
        CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;
        var from = period.Trim().ToLowerInvariant() switch
        {
            "7d" or "7days" or "week" => utcNow.AddDays(-7),
            "30d" or "30days" or "month" => utcNow.AddDays(-30),
            _ => utcNow.AddHours(-24),
        };

        var sensors = (await _uow.Sensors.GetAllAsync(ct)).AsEnumerable();
        var selectedAreas = await TechnicalReadAreasAsync(farmingAreaId, ct);
        if (selectedAreas is not null)
        {
            var allowedSensorIds = await SensorIdsInAreasAsync(selectedAreas, ct);
            sensors = sensors.Where(s => allowedSensorIds.Contains(s.Id));
        }
        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            sensors = sensors.Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value));
        }

        var sensorList = sensors.ToList();
        if (sensorList.Count == 0)
            return ApiResponse<IEnumerable<WaterQualitySnapshotDto>>.Ok(Array.Empty<WaterQualitySnapshotDto>());

        var sensorById = sensorList.ToDictionary(s => s.Id);
        var sensorIds = sensorById.Keys.ToList();
        var measurements = await _uow.WaterMeasurements.Query()
            .AsNoTracking()
            .Where(m => sensorIds.Contains(m.SensorId)
                && m.MeasuredAt >= from && m.MeasuredAt <= utcNow)
            .OrderBy(m => m.MeasuredAt)
            .ToListAsync(ct);

        var waterSystemAreas = (await _uow.WaterSystems.GetAllAsync(ct))
            .Where(item => item.FarmingAreaId.HasValue)
            .ToDictionary(item => item.Id, item => item.FarmingAreaId!.Value);
        var rows = (await _uow.FarmingRows.GetAllAsync(ct))
            .ToDictionary(item => item.Id);
        var devices = (await _uow.Devices.GetAllAsync(ct))
            .ToDictionary(item => item.Id);

        // One row is one physical sensor/metric/unit/minute. This deliberately avoids
        // combining different sensors or units into a synthetic farm-wide reading.
        var groups = measurements.GroupBy(m => new
        {
            m.SensorId,
            Bucket = new DateTime(m.MeasuredAt.Year, m.MeasuredAt.Month,
                m.MeasuredAt.Day, m.MeasuredAt.Hour, m.MeasuredAt.Minute, 0,
                DateTimeKind.Utc)
        });
        var snapshots = new List<WaterQualitySnapshotDto>();
        foreach (var group in groups)
        {
            var sensor = sensorById[group.Key.SensorId];
            var metric = NormalizeSensorType(sensor.SensorType);
            var value = group.OrderByDescending(item => item.MeasuredAt).First().Value;
            var area = SensorArea(sensor, waterSystemAreas, rows, devices);
            snapshots.Add(new WaterQualitySnapshotDto(
                Id: $"wq_{sensor.Id:N}_{group.Key.Bucket:yyyyMMddHHmm}",
                SensorId: sensor.Id.ToString(),
                FarmId: area?.ToString(),
                Temperature: metric == "temperature" ? value : 0,
                Ph: metric == "ph" ? value : 0,
                DissolvedOxygen: metric == "do" ? value : 0,
                Salinity: metric == "salinity" ? value : 0,
                Timestamp: group.Key.Bucket,
                IsAlertTriggered: false));
        }

        return ApiResponse<IEnumerable<WaterQualitySnapshotDto>>.Ok(snapshots);
    }

    private static Guid? SensorArea(
        Sensor sensor,
        IReadOnlyDictionary<Guid, Guid> waterSystemAreas,
        IReadOnlyDictionary<Guid, FarmingRow> rows,
        IReadOnlyDictionary<Guid, Device> devices)
    {
        if (sensor.WaterSystemId is Guid waterSystemId
            && waterSystemAreas.TryGetValue(waterSystemId, out var waterArea))
            return waterArea;
        if (sensor.FarmingRowId is Guid rowId && rows.TryGetValue(rowId, out var row))
            return row.FarmingAreaId;
        if (sensor.DeviceId is not Guid deviceId
            || !devices.TryGetValue(deviceId, out var device))
            return null;
        if (device.FarmingAreaId.HasValue) return device.FarmingAreaId;
        return device.FarmingRowId is Guid deviceRowId
            && rows.TryGetValue(deviceRowId, out var deviceRow)
                ? deviceRow.FarmingAreaId
                : null;
    }

    private async Task<HashSet<Guid>?> TechnicalReadAreasAsync(Guid? requestedAreaId, CancellationToken ct)
    {
        if (requestedAreaId is Guid areaId && areaId != Guid.Empty)
        {
            await _areas.EnsureReadAsync(areaId, ct);
            return [areaId];
        }

        var user = await _areas.CurrentUserAsync(ct);
        if (user is null || user.Role == UserRole.SystemAdministrator)
            return null;
        return await _areas.AreaIdsAsync(ct) ?? [];
    }

    private async Task EnsureSensorReadAsync(Guid sensorId, CancellationToken ct)
    {
        _ = await _uow.Sensors.GetByIdAsync(sensorId, ct) ?? throw AppException.NotFound("Sensor");
        var user = await _areas.CurrentUserAsync(ct);
        if (user is null || user.Role == UserRole.SystemAdministrator)
            return;

        var allowed = await _areas.AreaIdsAsync(ct) ?? [];
        var matching = await SensorIdsInAreasAsync(allowed, ct);
        if (!matching.Contains(sensorId))
            throw AppException.Forbidden("Bạn không được phân công khu của cảm biến này.");
    }

    private async Task<HashSet<Guid>> SensorIdsInAreasAsync(HashSet<Guid> areaIds, CancellationToken ct)
    {
        if (areaIds.Count == 0) return [];
        var waterSystemIds = (await _uow.WaterSystems.FindAsync(
            w => w.FarmingAreaId != null && areaIds.Contains(w.FarmingAreaId.Value), ct))
            .Select(w => w.Id).ToHashSet();
        var rowIds = (await _uow.FarmingRows.FindAsync(
            r => areaIds.Contains(r.FarmingAreaId), ct)).Select(r => r.Id).ToHashSet();
        var deviceIds = (await _uow.Devices.FindAsync(
            d => (d.FarmingAreaId != null && areaIds.Contains(d.FarmingAreaId.Value))
                 || (d.FarmingRowId != null && rowIds.Contains(d.FarmingRowId.Value)), ct))
            .Select(d => d.Id).ToHashSet();
        return (await _uow.Sensors.FindAsync(
            s => (s.WaterSystemId != null && waterSystemIds.Contains(s.WaterSystemId.Value))
                 || (s.FarmingRowId != null && rowIds.Contains(s.FarmingRowId.Value))
                 || (s.DeviceId != null && deviceIds.Contains(s.DeviceId.Value)), ct))
            .Select(s => s.Id).ToHashSet();
    }

    private static string NormalizeSensorType(string sensorType)
    {
        var t = sensorType.Trim().ToLowerInvariant();
        if (t.Contains("temp")) return "temperature";
        if (t is "ph" || t.Contains("ph")) return "ph";
        if (t is "do" || t.Contains("oxygen") || t.Contains("oxy")) return "do";
        if (t.Contains("salin") || t.Contains("salt")) return "salinity";
        if (t.Contains("tds")) return "tds";
        if (t is "ec" || t.Contains("conduct")) return "ec";
        return t;
    }

    // ─── Sensors CRUD ───────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<SensorDto>>> GetSensorsAsync(
        Guid? deviceId = null,
        Guid? farmingAreaId = null,
        Guid? farmingRowId = null,
        CancellationToken ct = default)
    {
        var sensors = (await _uow.Sensors.GetAllAsync(ct)).AsEnumerable();
        if (deviceId.HasValue)
            sensors = sensors.Where(s => s.DeviceId == deviceId.Value);

        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToList();
        var devices = (await _uow.Devices.GetAllAsync(ct)).ToDictionary(d => d.Id);

        if (farmingRowId is Guid rid && rid != Guid.Empty)
        {
            sensors = sensors.Where(s =>
                s.FarmingRowId == rid
                || (s.DeviceId is Guid did && devices.TryGetValue(did, out var d) && d.FarmingRowId == rid));
        }
        else if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var rowIds = rows.Where(r => r.FarmingAreaId == areaId).Select(r => r.Id).ToHashSet();
            sensors = sensors.Where(s =>
                (s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                || (s.FarmingRowId != null && rowIds.Contains(s.FarmingRowId.Value))
                || (s.DeviceId is Guid did && devices.TryGetValue(did, out var d) && (
                    d.FarmingAreaId == areaId
                    || (d.FarmingRowId != null && rowIds.Contains(d.FarmingRowId.Value)))));
        }

        return ApiResponse<IEnumerable<SensorDto>>.Ok(sensors.Select(MapSensor));
    }

    public async Task<ApiResponse<SensorDto>> GetSensorAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _uow.Sensors.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Sensor");
        return ApiResponse<SensorDto>.Ok(MapSensor(s));
    }

    public async Task<ApiResponse<SensorDto>> CreateSensorAsync(CreateSensorRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SensorCode))
            throw AppException.BadRequest("SensorCode is required.");
        if (string.IsNullOrWhiteSpace(req.SensorType))
            throw AppException.BadRequest("SensorType is required (pH, DO, Temperature, …).");

        if (await _uow.Sensors.AnyAsync(s => s.SensorCode == req.SensorCode.Trim(), ct))
            throw AppException.Conflict($"SensorCode '{req.SensorCode}' already exists.");

        if (req.DeviceId.HasValue)
            _ = await _uow.Devices.GetByIdAsync(req.DeviceId.Value, ct) ?? throw AppException.NotFound("Device");
        if (req.WaterSystemId.HasValue)
            _ = await _uow.WaterSystems.GetByIdAsync(req.WaterSystemId.Value, ct)
                ?? throw AppException.NotFound("WaterSystem");
        if (req.RasComponentId.HasValue)
            _ = await _uow.RasComponents.GetByIdAsync(req.RasComponentId.Value, ct)
                ?? throw AppException.NotFound("RasComponent");
        if (req.FarmingRowId is Guid rowId && rowId != Guid.Empty)
            _ = await _uow.FarmingRows.GetByIdAsync(rowId, ct)
                ?? throw AppException.NotFound("FarmingRow");

        var sensor = new Sensor
        {
            WaterSystemId = req.WaterSystemId,
            DeviceId = req.DeviceId,
            RasComponentId = req.RasComponentId,
            FarmingRowId = req.FarmingRowId is Guid r && r != Guid.Empty ? r : null,
            SensorCode = req.SensorCode.Trim(),
            SensorType = req.SensorType.Trim(),
            Unit = req.Unit,
            MinThreshold = req.MinThreshold,
            MaxThreshold = req.MaxThreshold,
            IsActive = true
        };
        await ValidateSensorLinksAsync(sensor, ct);
        await _uow.Sensors.AddAsync(sensor, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SensorDto>.Ok(MapSensor(sensor), "Sensor created.");
    }

    public async Task<ApiResponse<SensorDto>> UpdateSensorAsync(
        Guid id, UpdateSensorRequest req, CancellationToken ct = default)
    {
        var sensor = await _uow.Sensors.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Sensor");
        if (req.WaterSystemId.HasValue) sensor.WaterSystemId = req.WaterSystemId;
        if (req.DeviceId.HasValue) sensor.DeviceId = req.DeviceId;
        if (req.RasComponentId.HasValue) sensor.RasComponentId = req.RasComponentId;
        if (req.FarmingRowId.HasValue)
        {
            if (req.FarmingRowId.Value == Guid.Empty)
                sensor.FarmingRowId = null;
            else
            {
                _ = await _uow.FarmingRows.GetByIdAsync(req.FarmingRowId.Value, ct)
                    ?? throw AppException.NotFound("FarmingRow");
                sensor.FarmingRowId = req.FarmingRowId;
            }
        }
        if (req.SensorType is not null) sensor.SensorType = req.SensorType.Trim();
        if (req.Unit is not null) sensor.Unit = req.Unit;
        if (req.MinThreshold.HasValue) sensor.MinThreshold = req.MinThreshold;
        if (req.MaxThreshold.HasValue) sensor.MaxThreshold = req.MaxThreshold;
        if (req.IsActive.HasValue) sensor.IsActive = req.IsActive.Value;
        await ValidateSensorLinksAsync(sensor, ct);
        _uow.Sensors.Update(sensor);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SensorDto>.Ok(MapSensor(sensor), "Sensor updated.");
    }

    private async Task ValidateSensorLinksAsync(Sensor sensor, CancellationToken ct)
    {
        var areas = new HashSet<Guid>();
        if (sensor.WaterSystemId is Guid waterSystemId)
        {
            var waterSystem = await _uow.WaterSystems.GetByIdAsync(waterSystemId, ct)
                ?? throw AppException.NotFound("WaterSystem");
            if (waterSystem.FarmingAreaId is Guid areaId) areas.Add(areaId);
        }
        if (sensor.FarmingRowId is Guid rowId)
        {
            var row = await _uow.FarmingRows.GetByIdAsync(rowId, ct)
                ?? throw AppException.NotFound("FarmingRow");
            areas.Add(row.FarmingAreaId);
        }
        if (sensor.DeviceId is Guid deviceId)
        {
            var device = await _uow.Devices.GetByIdAsync(deviceId, ct)
                ?? throw AppException.NotFound("Device");
            if (device.FarmingAreaId is Guid areaId) areas.Add(areaId);
            if (device.FarmingRowId is Guid deviceRowId)
            {
                var row = await _uow.FarmingRows.GetByIdAsync(deviceRowId, ct)
                    ?? throw AppException.NotFound("FarmingRow");
                areas.Add(row.FarmingAreaId);
            }
        }
        if (areas.Count > 1)
            throw AppException.BadRequest(
                "Sensor, Device, FarmingRow và WaterSystem phải thuộc cùng FarmingArea.");
    }

    public async Task<ApiResponse> DeleteSensorAsync(Guid id, CancellationToken ct = default)
    {
        var sensor = await _uow.Sensors.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Sensor");
        var hasData = await _uow.WaterMeasurements.AnyAsync(m => m.SensorId == id, ct);
        if (hasData)
            throw AppException.Conflict("Sensor has measurements — deactivate (IsActive=false) instead of delete.");
        _uow.Sensors.Remove(sensor);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Sensor deleted.");
    }

    // ─── Devices CRUD ───────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<DeviceDto>>> GetDevicesAsync(
        Guid? farmingAreaId = null,
        Guid? farmingRowId = null,
        CancellationToken ct = default)
    {
        var devices = (await _uow.Devices.GetAllAsync(ct)).AsEnumerable();
        var sensors = (await _uow.Sensors.GetAllAsync(ct)).ToList();
        var allSensors = sensors;
        var actuators = (await _uow.RasComponents.FindAsync(c => c.RelayDeviceId != null, ct)).ToList();
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var waterSystems = (await _uow.WaterSystems.GetAllAsync(ct))
            .ToDictionary(w => w.Id);

        var currentUser = await _areas.CurrentUserAsync(ct);
        if (currentUser is not null
            && currentUser.Role != UserRole.SystemAdministrator)
        {
            var assigned = await _areas.AreaIdsAsync(ct)
                ?? throw AppException.Unauthorized();
            var selected = assigned;
            if (farmingAreaId is Guid requestedArea && requestedArea != Guid.Empty)
            {
                if (!assigned.Contains(requestedArea))
                    throw AppException.Forbidden("Bạn không được phân công khu này.");
                selected = [requestedArea];
            }

            HashSet<Guid> DeviceAreas(Device device)
            {
                var result = new HashSet<Guid>();
                if (device.FarmingAreaId is Guid direct)
                    result.Add(direct);
                if (device.FarmingRowId is Guid rowId
                    && rows.TryGetValue(rowId, out var row))
                    result.Add(row.FarmingAreaId);
                foreach (var sensor in sensors.Where(s => s.DeviceId == device.Id))
                {
                    if (sensor.FarmingRowId is Guid sensorRowId
                        && rows.TryGetValue(sensorRowId, out var sensorRow))
                        result.Add(sensorRow.FarmingAreaId);
                    if (sensor.WaterSystemId is Guid waterSystemId
                        && waterSystems.TryGetValue(waterSystemId, out var waterSystem)
                        && waterSystem.FarmingAreaId is Guid waterArea)
                        result.Add(waterArea);
                }
                foreach (var component in actuators.Where(c => c.RelayDeviceId == device.Id))
                    if (waterSystems.TryGetValue(component.WaterSystemId, out var waterSystem)
                        && waterSystem.FarmingAreaId is Guid waterArea)
                        result.Add(waterArea);
                return result;
            }

            devices = devices.Where(device =>
            {
                var linkedAreas = DeviceAreas(device);
                return linkedAreas.Count > 0
                    && linkedAreas.All(selected.Contains);
            });
        }

        if (farmingRowId is Guid rid && rid != Guid.Empty)
        {
            if (!rows.TryGetValue(rid, out var requestedRow))
                throw AppException.NotFound("FarmingRow");
            if (currentUser is not null
                && currentUser.Role != UserRole.SystemAdministrator)
                await _areas.EnsureReadAsync(requestedRow.FarmingAreaId, ct);
            devices = devices.Where(d => d.FarmingRowId == rid);
        }

        var keptIds = devices.Select(d => d.Id).ToHashSet();
        var countBy = allSensors.Where(s => s.DeviceId.HasValue && keptIds.Contains(s.DeviceId.Value))
            .GroupBy(s => s.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var actBy = actuators
            .Where(c => c.RelayDeviceId != null)
            .GroupBy(c => c.RelayDeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return ApiResponse<IEnumerable<DeviceDto>>.Ok(
            devices.Select(d => MapDevice(
                d,
                countBy.GetValueOrDefault(d.Id),
                actBy.GetValueOrDefault(d.Id),
                d.FarmingAreaId is Guid aid ? areas.GetValueOrDefault(aid) : null,
                d.FarmingRowId is Guid rid ? rows.GetValueOrDefault(rid) : null)));
    }

    private async Task<FarmingRow?> RowOfAsync(Device d, CancellationToken ct) =>
        d.FarmingRowId is Guid rid ? await _uow.FarmingRows.GetByIdAsync(rid, ct) : null;

    /// <summary>Gán dãy cho thiết bị; Guid.Empty = gỡ. Đồng bộ FarmingAreaId theo dãy.</summary>
    private async Task ApplyRowAsync(Device device, Guid? farmingRowId, CancellationToken ct)
    {
        if (farmingRowId is null) return;
        if (farmingRowId.Value == Guid.Empty)
        {
            device.FarmingRowId = null;
            return;
        }
        var row = await _uow.FarmingRows.GetByIdAsync(farmingRowId.Value, ct)
            ?? throw AppException.NotFound("FarmingRow");
        if (device.FarmingAreaId is Guid aid && row.FarmingAreaId != aid)
            throw AppException.BadRequest("Dãy không thuộc khu vực của Controller.");
        device.FarmingRowId = row.Id;
        device.FarmingAreaId ??= row.FarmingAreaId;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? Limit(string? v, int max)
    {
        if (v is null) return null;
        if (v.Length > max)
            throw AppException.BadRequest($"Trường vượt quá {max} ký tự.");
        return v;
    }

    public async Task<ApiResponse<DeviceDetailDto>> GetDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        await EnsureDeviceReadAsync(d, ct);
        var sensorEntities = (await _uow.Sensors.FindAsync(s => s.DeviceId == id, ct))
            .OrderBy(s => s.SensorCode)
            .ToList();
        var sensorIds = sensorEntities.Select(s => s.Id).ToList();
        var latestBySensor = sensorIds.Count == 0
            ? new Dictionary<Guid, WaterMeasurement>()
            : (await _uow.WaterMeasurements.FindAsync(m => sensorIds.Contains(m.SensorId), ct))
                .GroupBy(m => m.SensorId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(m => m.MeasuredAt).First());
        var sensors = sensorEntities.Select(s =>
        {
            latestBySensor.TryGetValue(s.Id, out var latest);
            return new SensorDto(
                s.Id, s.WaterSystemId, s.DeviceId, s.SensorCode, s.SensorType, s.Unit,
                s.MinThreshold, s.MaxThreshold, s.IsActive, s.LastSeenAt, s.RasComponentId,
                s.FarmingRowId, latest?.Value, latest?.MeasuredAt);
        }).ToList();
        var actuators = (await _uow.RasComponents.FindAsync(c => c.RelayDeviceId == id, ct))
            .OrderBy(c => c.Position)
            .Select(c => new DeviceActuatorDto(
                c.Id, c.Code, c.Name, c.Type, c.IsOn, c.ControlMode, c.RelayChannel))
            .ToList();
        FarmingArea? area = null;
        if (d.FarmingAreaId is Guid aid)
            area = await _uow.FarmingAreas.GetByIdAsync(aid, ct);
        return ApiResponse<DeviceDetailDto>.Ok(
            MapDeviceDetail(d, sensors, actuators, area, await RowOfAsync(d, ct)));
    }

    private async Task EnsureDeviceReadAsync(Device device, CancellationToken ct)
    {
        var user = await _areas.CurrentUserAsync(ct);
        if (user is null || user.Role == UserRole.SystemAdministrator)
            return;

        var linkedAreas = new HashSet<Guid>();
        if (device.FarmingAreaId is Guid directArea)
        {
            await _areas.EnsureReadAsync(directArea, ct);
            linkedAreas.Add(directArea);
        }
        if (device.FarmingRowId is Guid rowId)
        {
            var row = await _uow.FarmingRows.GetByIdAsync(rowId, ct);
            if (row is not null)
                linkedAreas.Add(row.FarmingAreaId);
        }

        var sensors = await _uow.Sensors.FindAsync(
            sensor => sensor.DeviceId == device.Id, ct);
        foreach (var sensor in sensors)
        {
            if (sensor.FarmingRowId is Guid sensorRowId)
            {
                var row = await _uow.FarmingRows.GetByIdAsync(sensorRowId, ct);
                if (row is not null)
                    linkedAreas.Add(row.FarmingAreaId);
            }
            if (sensor.WaterSystemId is Guid waterSystemId)
            {
                var waterSystem = await _uow.WaterSystems.GetByIdAsync(
                    waterSystemId, ct);
                if (waterSystem?.FarmingAreaId is Guid waterArea)
                    linkedAreas.Add(waterArea);
            }
        }

        var relays = await _uow.RasComponents.FindAsync(
            component => component.RelayDeviceId == device.Id, ct);
        foreach (var relay in relays)
        {
            var waterSystem = await _uow.WaterSystems.GetByIdAsync(
                relay.WaterSystemId, ct);
            if (waterSystem?.FarmingAreaId is Guid waterArea)
                linkedAreas.Add(waterArea);
        }

        if (linkedAreas.Count == 0)
            throw AppException.Forbidden(
                "Thiết bị chưa có khu sở hữu được xác minh.");
        await _areas.EnsureAllAssignedAsync(linkedAreas, ct);
    }

    public async Task<ApiResponse<DeviceDto>> CreateDeviceAsync(
        CreateDeviceRequest req, CancellationToken ct = default, Guid? actorId = null, string? ip = null)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceCode))
            throw AppException.BadRequest("DeviceCode is required (ESP32 id / MAC).");
        var code = req.DeviceCode.Trim();
        var codeKey = code.ToLowerInvariant();
        if (await _uow.Devices.AnyAsync(d => d.DeviceCode.ToLower() == codeKey, ct))
            throw AppException.Conflict($"DeviceCode '{code}' already exists.");
        var deviceIp = string.IsNullOrWhiteSpace(req.IpAddress) ? null : req.IpAddress.Trim();
        if (deviceIp is not null && await _uow.Devices.AnyAsync(d => d.IpAddress == deviceIp, ct))
            throw AppException.Conflict($"IP '{deviceIp}' đang được thiết bị khác sử dụng.");

        if (req.FarmingAreaId is Guid areaId && areaId != Guid.Empty)
            _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
                ?? throw AppException.NotFound("FarmingArea");

        var device = new Device
        {
            DeviceCode = code,
            Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim(),
            DeviceType = string.IsNullOrWhiteSpace(req.DeviceType) ? "esp32" : req.DeviceType.Trim().ToLowerInvariant(),
            MacAddress = string.IsNullOrWhiteSpace(req.MacAddress) ? null : req.MacAddress.Trim(),
            IpAddress = deviceIp,
            FarmingAreaId = req.FarmingAreaId is Guid a && a != Guid.Empty ? a : null,
            FirmwareVersion = req.FirmwareVersion,
            ApiKey = req.ApiKey,
            StreamUrl = Clean(req.StreamUrl),
            SnapshotUrl = Clean(req.SnapshotUrl),
            Resolution = Clean(req.Resolution),
            InstallationLocation = Limit(Clean(req.InstallationLocation), 200),
            Notes = Limit(Clean(req.Note ?? req.Notes), 500),
            Status = DeviceStatus.Offline
        };
        await ApplyRowAsync(device, req.FarmingRowId, ct);
        await _uow.Devices.AddAsync(device, ct);
        await WriteDeviceAuditAsync(actorId, "CreateDevice", device.Id, device.DeviceCode, ip, ct);
        await _uow.SaveChangesAsync(ct);
        FarmingArea? area = null;
        if (device.FarmingAreaId is Guid aid)
            area = await _uow.FarmingAreas.GetByIdAsync(aid, ct);
        return ApiResponse<DeviceDto>.Ok(
            MapDevice(device, 0, 0, area, await RowOfAsync(device, ct)), "Controller/ESP32 registered.");
    }

    public async Task<ApiResponse<DeviceDto>> UpdateDeviceAsync(
        Guid id, UpdateDeviceRequest req, CancellationToken ct = default, Guid? actorId = null, string? ip = null)
    {
        var device = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        if (req.DeviceType is not null) device.DeviceType = req.DeviceType.Trim().ToLowerInvariant();
        if (req.FirmwareVersion is not null) device.FirmwareVersion = req.FirmwareVersion;
        if (req.BatteryLevel.HasValue) device.BatteryLevel = req.BatteryLevel;
        if (req.RssiDbm.HasValue) device.RssiDbm = req.RssiDbm;
        if (req.ApiKey is not null) device.ApiKey = req.ApiKey;
        if (req.Name is not null) device.Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();
        if (req.MacAddress is not null)
            device.MacAddress = string.IsNullOrWhiteSpace(req.MacAddress) ? null : req.MacAddress.Trim();
        if (req.IpAddress is not null)
        {
            var nextIp = string.IsNullOrWhiteSpace(req.IpAddress) ? null : req.IpAddress.Trim();
            if (nextIp is not null && await _uow.Devices.AnyAsync(d => d.Id != id && d.IpAddress == nextIp, ct))
                throw AppException.Conflict($"IP '{nextIp}' đang được thiết bị khác sử dụng.");
            device.IpAddress = nextIp;
        }
        if (req.FarmingAreaId.HasValue)
        {
            if (req.FarmingAreaId.Value == Guid.Empty)
                device.FarmingAreaId = null;
            else
            {
                _ = await _uow.FarmingAreas.GetByIdAsync(req.FarmingAreaId.Value, ct)
                    ?? throw AppException.NotFound("FarmingArea");
                if (device.FarmingAreaId != req.FarmingAreaId)
                {
                    if (req.FarmingRowId is null && device.FarmingRowId is Guid currentRowId)
                    {
                        var currentRow = await _uow.FarmingRows.GetByIdAsync(currentRowId, ct);
                        if (currentRow is not null && currentRow.FarmingAreaId != req.FarmingAreaId)
                            device.FarmingRowId = null;
                    }
                }
                device.FarmingAreaId = req.FarmingAreaId;
            }
        }
        await ApplyRowAsync(device, req.FarmingRowId, ct);
        if (req.StreamUrl is not null) device.StreamUrl = Clean(req.StreamUrl);
        if (req.SnapshotUrl is not null) device.SnapshotUrl = Clean(req.SnapshotUrl);
        if (req.Resolution is not null) device.Resolution = Clean(req.Resolution);
        if (req.InstallationLocation is not null)
            device.InstallationLocation = Limit(Clean(req.InstallationLocation), 200);
        if (req.Note is not null || req.Notes is not null)
            device.Notes = Limit(Clean(req.Note ?? req.Notes), 500);
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            if (!Enum.TryParse<DeviceStatus>(req.Status, true, out var st) || !Enum.IsDefined(st))
                throw AppException.BadRequest("Trạng thái thiết bị không hợp lệ.");
            device.Status = st;
        }
        _uow.Devices.Update(device);
        await WriteDeviceAuditAsync(actorId, "UpdateDevice", device.Id, device.DeviceCode, ip, ct);
        await _uow.SaveChangesAsync(ct);
        var n = await _uow.Sensors.CountAsync(s => s.DeviceId == id, ct);
        var acts = await _uow.RasComponents.CountAsync(c => c.RelayDeviceId == id, ct);
        FarmingArea? area = null;
        if (device.FarmingAreaId is Guid aid)
            area = await _uow.FarmingAreas.GetByIdAsync(aid, ct);
        return ApiResponse<DeviceDto>.Ok(
            MapDevice(device, n, acts, area, await RowOfAsync(device, ct)), "Controller updated.");
    }

    public async Task<ApiResponse> DeleteDeviceAsync(
        Guid id, CancellationToken ct = default, Guid? actorId = null, string? ip = null)
    {
        var device = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        foreach (var sensor in await _uow.Sensors.FindAsync(s => s.DeviceId == id, ct))
            sensor.DeviceId = null;
        foreach (var relay in await _uow.RasComponents.FindAsync(c => c.RelayDeviceId == id, ct))
        {
            relay.RelayDeviceId = null;
            relay.RelayChannel = null;
            relay.HasRelay = false;
            relay.IsOn = false;
            relay.ControlMode = null;
        }
        foreach (var row in await _uow.Hdf5Uploads.FindAsync(x => x.DeviceId == id, ct))
            row.DeviceId = null;
        foreach (var row in await _uow.MediaAssets.FindAsync(x => x.DeviceId == id, ct))
            row.DeviceId = null;
        foreach (var row in await _uow.AiDetections.FindAsync(x => x.DeviceId == id, ct))
            row.DeviceId = null;
        await _uow.SaveChangesAsync(ct);
        await WriteDeviceAuditAsync(actorId, "DeleteDevice", device.Id, device.DeviceCode, ip, ct);
        _uow.Devices.Remove(device);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Device deleted.");
    }

    public async Task<ApiResponse<DeviceDto>> UpdateDeviceStatusAsync(
        Guid deviceId, UpdateDeviceStatusRequest req, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct) ?? throw AppException.NotFound("Device");
        if (device.Status == DeviceStatus.Maintenance)
            throw AppException.Forbidden("Thiết bị đang bảo trì; chỉ quản trị viên được thay đổi trạng thái.");
        if (!Enum.TryParse<DeviceStatus>(req.Status, true, out var status)
            || status is not (DeviceStatus.Online or DeviceStatus.Offline or DeviceStatus.Error))
            throw AppException.BadRequest("Thiết bị chỉ được báo Online, Offline hoặc Error.");
        if (req.BatteryLevel is < 0 or > 100)
            throw AppException.BadRequest("BatteryLevel phải trong khoảng 0–100.");
        if (!string.IsNullOrWhiteSpace(req.MacAddress)
            && !string.Equals(req.MacAddress.Trim(), device.MacAddress, StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden("MAC là định danh cấu hình; thiết bị không được tự thay đổi.");
        device.Status = status;
        device.BatteryLevel = req.BatteryLevel ?? device.BatteryLevel;
        device.RssiDbm = req.RssiDbm ?? device.RssiDbm;
        if (!string.IsNullOrWhiteSpace(req.IpAddress)) device.IpAddress = req.IpAddress.Trim();
        device.LastSeenAt = DateTime.UtcNow;
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);
        var n = await _uow.Sensors.CountAsync(s => s.DeviceId == deviceId, ct);
        var acts = await _uow.RasComponents.CountAsync(c => c.RelayDeviceId == deviceId, ct);
        FarmingArea? area = null;
        if (device.FarmingAreaId is Guid aid)
            area = await _uow.FarmingAreas.GetByIdAsync(aid, ct);
        return ApiResponse<DeviceDto>.Ok(MapDevice(device, n, acts, area, await RowOfAsync(device, ct)));
    }

    // ─── Water systems ──────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<WaterSystemDto>>> GetWaterSystemsAsync(CancellationToken ct = default)
    {
        var list = (await _uow.WaterSystems.GetAllAsync(ct) ?? Enumerable.Empty<WaterSystem>()).AsEnumerable();
        var user = await _areas.CurrentUserAsync(ct);
        if (user is not null && user.Role != UserRole.SystemAdministrator)
        {
            var scope = await _areas.AreaIdsAsync(ct) ?? new HashSet<Guid>();
            list = list.Where(ws => ws.FarmingAreaId is Guid areaId && scope.Contains(areaId));
        }
        return ApiResponse<IEnumerable<WaterSystemDto>>.Ok(list.Select(MapWs));
    }

    public async Task<ApiResponse<WaterSystemDto>> CreateWaterSystemAsync(
        CreateWaterSystemRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
        if (req.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
        {
            if (await _areas.CurrentUserAsync(ct) is not null)
                throw AppException.BadRequest("FarmingAreaId is required.");
        }
        else
        {
            _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct) ?? throw AppException.NotFound("FarmingArea");
            await _areas.EnsureAsync(areaId, manage: true, ct);
        }
        var ws = new WaterSystem
        {
            Name = req.Name.Trim(),
            FarmingAreaId = req.FarmingAreaId,
            Type = req.Type,
            IsActive = true,
            Status = string.IsNullOrWhiteSpace(req.Status) ? "active" : req.Status.Trim(),
            FlowStatus = req.FlowStatus,
            Description = req.Description
        };
        await _uow.WaterSystems.AddAsync(ws, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<WaterSystemDto>.Ok(MapWs(ws), "Water system created.");
    }

    public async Task<ApiResponse<WaterSystemDto>> UpdateWaterSystemAsync(
        Guid id, UpdateWaterSystemRequest req, CancellationToken ct = default)
    {
        var ws = await _uow.WaterSystems.GetByIdAsync(id, ct) ?? throw AppException.NotFound("WaterSystem");
        if (ws.FarmingAreaId is Guid currentArea)
            await _areas.EnsureAsync(currentArea, manage: true, ct);
        if (req.FarmingAreaId is Guid nextArea && nextArea != Guid.Empty && nextArea != ws.FarmingAreaId)
        {
            _ = await _uow.FarmingAreas.GetByIdAsync(nextArea, ct) ?? throw AppException.NotFound("FarmingArea");
            await _areas.EnsureAsync(nextArea, manage: true, ct);
        }
        if (req.Name is not null) ws.Name = req.Name.Trim();
        if (req.FarmingAreaId.HasValue) ws.FarmingAreaId = req.FarmingAreaId;
        if (req.Type is not null) ws.Type = req.Type;
        if (req.IsActive.HasValue) ws.IsActive = req.IsActive.Value;
        if (req.Status is not null) ws.Status = req.Status.Trim();
        if (req.FlowStatus is not null) ws.FlowStatus = req.FlowStatus;
        if (req.Description is not null) ws.Description = req.Description;
        _uow.WaterSystems.Update(ws);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<WaterSystemDto>.Ok(MapWs(ws), "Water system updated.");
    }

    public async Task<ApiResponse> DeleteWaterSystemAsync(Guid id, CancellationToken ct = default)
    {
        var ws = await _uow.WaterSystems.GetByIdAsync(id, ct) ?? throw AppException.NotFound("WaterSystem");
        if (ws.FarmingAreaId is Guid deleteArea)
            await _areas.EnsureAsync(deleteArea, manage: true, ct);
        if (await _uow.Sensors.AnyAsync(s => s.WaterSystemId == id, ct))
            throw AppException.Conflict("Water system still has sensors.");
        var flows = (await _uow.WaterFlows.FindAsync(f => f.WaterSystemId == id, ct)).ToList();
        foreach (var f in flows) _uow.WaterFlows.Remove(f);
        var comps = (await _uow.RasComponents.FindAsync(c => c.WaterSystemId == id, ct)).ToList();
        foreach (var c in comps) _uow.RasComponents.Remove(c);
        _uow.WaterSystems.Remove(ws);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Water system deleted.");
    }

    public async Task<ApiResponse<string>> ProcessHdf5UploadAsync(
        Hdf5UploadRequest req,
        Stream fileStream,
        Guid authenticatedDeviceId,
        string authenticatedDeviceCode,
        CancellationToken ct = default)
    {
        if (authenticatedDeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(authenticatedDeviceCode)
            || !string.Equals(
                req.DeviceCode?.Trim(),
                authenticatedDeviceCode,
                StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden(
                "Device không được upload dữ liệu thay cho device khác.");

        var device = await _uow.Devices.GetByIdAsync(authenticatedDeviceId, ct)
            ?? throw AppException.NotFound("Device");
        if (!string.Equals(
                device.DeviceCode,
                authenticatedDeviceCode,
                StringComparison.OrdinalIgnoreCase))
            throw AppException.Forbidden("Device credential không khớp dữ liệu upload.");

        var safeFileName = Path.GetFileName(req.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName)
            || !string.Equals(safeFileName, req.FileName, StringComparison.Ordinal)
            || safeFileName.Any(char.IsControl)
            || Path.GetExtension(safeFileName).ToLowerInvariant() is not (".h5" or ".hdf5"))
            throw AppException.BadRequest("Tên file HDF5 không hợp lệ.");
        if (req.FileSizeBytes <= 0 || req.FileSizeBytes > 512L * 1024 * 1024)
            throw AppException.BadRequest("Kích thước HDF5 phải trong khoảng 1 byte đến 512 MiB.");
        var expectedChecksum = req.Checksum?.Trim();
        if (string.IsNullOrWhiteSpace(expectedChecksum)
            || expectedChecksum.Length != 64
            || expectedChecksum.Any(c => !Uri.IsHexDigit(c)))
            throw AppException.BadRequest("Checksum phải là SHA-256 dạng hex 64 ký tự.");

        Stream uploadStream = fileStream;
        FileStream? temporary = null;
        try
        {
            if (!fileStream.CanSeek)
            {
                temporary = new FileStream(
                    Path.GetTempFileName(),
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    81_920,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                await fileStream.CopyToAsync(temporary, ct);
                temporary.Position = 0;
                uploadStream = temporary;
            }

            var start = uploadStream.Position;
            var actualChecksum = Convert.ToHexString(
                await SHA256.HashDataAsync(uploadStream, ct));
            var actualSize = uploadStream.Position - start;
            uploadStream.Position = start;
            if (actualSize != req.FileSizeBytes)
                throw AppException.BadRequest(
                    "Kích thước file thực tế không khớp metadata.");
            if (!string.Equals(
                    actualChecksum, expectedChecksum,
                    StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest(
                    "SHA-256 của file không khớp checksum khai báo.");

            var deviceSegment = Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(device.DeviceCode)))
                .ToLowerInvariant()[..16];
            var objectName =
                $"hdf5/{deviceSegment}/{req.ChunkStartTime:yyyyMMddHHmmss}_{Guid.NewGuid():N}{Path.GetExtension(safeFileName).ToLowerInvariant()}";
            var storagePath = await _storage.UploadAsync(
                "crabsense-hdf5", objectName, uploadStream,
                "application/octet-stream", ct);

            var upload = new Hdf5Upload
            {
                DeviceId = device.Id,
                FileName = safeFileName,
                StoragePath = storagePath,
                Checksum = actualChecksum,
                FileSizeBytes = actualSize,
                ChunkStartTime = req.ChunkStartTime,
                ChunkEndTime = req.ChunkEndTime,
                Status = "pending"
            };
            await _uow.Hdf5Uploads.AddAsync(upload, ct);

            device.LastSeenAt = DateTime.UtcNow;
            device.Status = DeviceStatus.Online;
            _uow.Devices.Update(device);

            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch
            {
                await _storage.DeleteAsync(
                    "crabsense-hdf5", objectName, CancellationToken.None);
                throw;
            }
            return ApiResponse<string>.Ok(
                storagePath, "HDF5 uploaded successfully.");
        }
        finally
        {
            if (temporary is not null)
                await temporary.DisposeAsync();
        }
    }

    private static SensorDto MapSensor(Sensor s) =>
        new(s.Id, s.WaterSystemId, s.DeviceId, s.SensorCode, s.SensorType, s.Unit,
            s.MinThreshold, s.MaxThreshold, s.IsActive, s.LastSeenAt, s.RasComponentId,
            s.FarmingRowId);

    private static (string Type, string Unit)? MeterSpec(string? code) => code?.Trim() switch
    {
        "meter_v" => ("Voltage", "V"),
        "meter_a" => ("Current", "A"),
        "meter_w" => ("Power", "W"),
        "meter_va" => ("ApparentPower", "VA"),
        "meter_kwh" => ("Energy", "kWh"),
        "meter_hz" => ("Frequency", "Hz"),
        "meter_pf" => ("PowerFactor", "%"),
        "meter_min" => ("Runtime", "min"),
        "meter_c" => ("Temperature", "C"),
        // UPS (CyberPower USB HID) — thiết bị riêng, không phải ESP32/ACD301.
        // Cho phép tự đăng ký cảm biến ở lần ingest đầu, giống nhóm meter_*.
        "ups_input_voltage" => ("Voltage", "V"),
        "ups_output_voltage" => ("Voltage", "V"),
        "ups_battery_voltage" => ("Voltage", "V"),
        "ups_load" => ("PercentLoad", "%"),
        "ups_battery_charge" => ("BatteryCharge", "%"),
        "ups_battery_runtime" => ("Runtime", "s"),
        "ups_status_online" => ("State", "state"),
        "ups_status_charging" => ("State", "state"),
        "ups_status_discharging" => ("State", "state"),
        "ups_status_low_battery" => ("State", "state"),
        "ups_output_power" => ("Power", "W"),
        "ups_output_apparent_power" => ("ApparentPower", "VA"),
        "ups_output_current" => ("Current", "A"),
        "ups_input_power" => ("Power", "W"),
        "ups_input_apparent_power" => ("ApparentPower", "VA"),
        "ups_input_current" => ("Current", "A"),
        _ => null
    };

    /// <summary>
    /// URL stream hiệu lực của camera: StreamUrl khai báo → FirmwareVersion là URL (cách cũ)
    /// → suy từ IpAddress theo quy ước ESP32-CAM (http://ip/stream). Không phải camera → null.
    /// </summary>
    private static (string? stream, string? snapshot) ResolveCameraUrls(Device d)
    {
        var isCamera = (d.DeviceType ?? "").Contains("cam", StringComparison.OrdinalIgnoreCase);
        if (!isCamera) return (d.StreamUrl, d.SnapshotUrl);

        var stream = d.StreamUrl;
        if (string.IsNullOrWhiteSpace(stream) && !string.IsNullOrWhiteSpace(d.FirmwareVersion))
        {
            var fw = d.FirmwareVersion.Trim();
            if (fw.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                || fw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || fw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                stream = fw;
        }

        var snapshot = d.SnapshotUrl;
        if (!string.IsNullOrWhiteSpace(d.IpAddress))
        {
            var ip = d.IpAddress.Trim();
            var host = ip.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ip.TrimEnd('/') : $"http://{ip}";
            stream ??= $"{host}/stream";
            snapshot ??= $"{host}/capture";
        }
        return (stream, snapshot);
    }

    private static DeviceDto MapDevice(
        Device d, int sensorCount, int actuatorCount = 0, FarmingArea? area = null, FarmingRow? row = null)
    {
        var (stream, snapshot) = ResolveCameraUrls(d);
        return new(d.Id, d.DeviceCode, string.IsNullOrWhiteSpace(d.DeviceType) ? "esp32" : d.DeviceType,
            d.FirmwareVersion, d.BatteryLevel, d.RssiDbm,
            d.Status.ToString(), d.LastSeenAt, sensorCount,
            string.IsNullOrWhiteSpace(d.Name) ? d.DeviceCode : d.Name,
            d.MacAddress, d.IpAddress, d.FarmingAreaId,
            area?.Name, area?.Code, actuatorCount,
            d.FarmingRowId, row?.Name, row?.Code,
            stream, snapshot, d.Resolution,
            d.InstallationLocation, d.Notes);
    }

    private static DeviceDetailDto MapDeviceDetail(
        Device d,
        IReadOnlyList<SensorDto> sensors,
        IReadOnlyList<DeviceActuatorDto> actuators,
        FarmingArea? area,
        FarmingRow? row = null)
    {
        var (stream, snapshot) = ResolveCameraUrls(d);
        return new(d.Id, d.DeviceCode,
            string.IsNullOrWhiteSpace(d.DeviceType) ? "esp32" : d.DeviceType,
            d.FirmwareVersion, d.Status.ToString(), d.LastSeenAt,
            sensors.Count, actuators.Count,
            string.IsNullOrWhiteSpace(d.Name) ? d.DeviceCode : d.Name,
            d.MacAddress, d.IpAddress, d.FarmingAreaId,
            area?.Name, area?.Code, d.BatteryLevel, d.RssiDbm,
            sensors, actuators,
            d.FarmingRowId, row?.Name, row?.Code,
            stream, snapshot, d.Resolution,
            d.InstallationLocation, d.Notes);
    }

    private static WaterSystemDto MapWs(WaterSystem w) =>
        new(w.Id, w.FarmingAreaId, w.Name, w.Type, w.IsActive,
            string.IsNullOrWhiteSpace(w.Status) ? "active" : w.Status, w.FlowStatus, w.Description);
}
