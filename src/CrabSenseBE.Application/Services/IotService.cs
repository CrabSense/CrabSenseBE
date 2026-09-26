using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// IoT: ESP32 gateway (Device) → Sensors → HTTP ingest → kiosk live view.
/// </summary>
public class IotService : IIotService
{
    private readonly IUnitOfWork _uow;
    private readonly IStorageService _storage;
    private readonly IAlertService _alerts;

    public IotService(IUnitOfWork uow, IStorageService storage, IAlertService alerts)
    {
        _uow = uow;
        _storage = storage;
        _alerts = alerts;
    }

    // ─── Ingest (ESP32 HTTP) ────────────────────────────────────────────────

    public async Task<ApiResponse> IngestSensorDataAsync(SensorDataRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SensorCode))
            throw AppException.BadRequest("SensorCode is required.");

        var sensor = await _uow.Sensors.FirstOrDefaultAsync(s => s.SensorCode == req.SensorCode, ct)
            ?? throw AppException.NotFound($"Sensor '{req.SensorCode}' — register it first (POST /api/sensors).");

        // Upsert device (ESP32) by DeviceCode so first heartbeat doesn't 404
        Device? device = null;
        if (!string.IsNullOrWhiteSpace(req.DeviceCode))
        {
            device = await _uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == req.DeviceCode, ct);
            if (device is null)
            {
                device = new Device
                {
                    DeviceCode = req.DeviceCode.Trim(),
                    Status = DeviceStatus.Online,
                    LastSeenAt = DateTime.UtcNow,
                    IpAddress = string.IsNullOrWhiteSpace(req.IpAddress) ? null : req.IpAddress.Trim()
                };
                await _uow.Devices.AddAsync(device, ct);
            }
            else
            {
                device.LastSeenAt = DateTime.UtcNow;
                device.Status = DeviceStatus.Online;
                if (!string.IsNullOrWhiteSpace(req.IpAddress))
                    device.IpAddress = req.IpAddress.Trim();
                _uow.Devices.Update(device);
            }

            if (sensor.DeviceId is null || sensor.DeviceId != device.Id)
            {
                sensor.DeviceId = device.Id;
            }
        }

        var measurement = new WaterMeasurement
        {
            SensorId = sensor.Id,
            WaterSystemId = sensor.WaterSystemId,
            Value = req.Value,
            Unit = req.Unit ?? sensor.Unit,
            MeasuredAt = req.MeasuredAt == default ? DateTime.UtcNow : req.MeasuredAt,
            Source = "realtime"
        };
        await _uow.WaterMeasurements.AddAsync(measurement, ct);

        sensor.LastSeenAt = DateTime.UtcNow;
        _uow.Sensors.Update(sensor);
        await _uow.SaveChangesAsync(ct);

        await _alerts.EvaluateMeasurementAsync(sensor, req.Value, ct);
        return ApiResponse.Ok("Sensor data ingested.");
    }

    public async Task<ApiResponse> IngestSensorDataBatchAsync(SensorDataBatchRequest req, CancellationToken ct = default)
    {
        var list = req.Measurements?.ToList() ?? new List<SensorDataRequest>();
        foreach (var item in list)
            await IngestSensorDataAsync(item, ct);
        return ApiResponse.Ok($"Batch of {list.Count} records ingested.");
    }

    public async Task<ApiResponse<PagedResult<SensorDataDto>>> GetSensorDataAsync(
        Guid sensorId, DateTime? from, DateTime? to, int page, int pageSize, CancellationToken ct = default)
    {
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
        _ = await _uow.Sensors.GetByIdAsync(sensorId, ct) ?? throw AppException.NotFound("Sensor");
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
        var allMeas = await _uow.WaterMeasurements.GetAllAsync(ct);
        var latestBySensor = allMeas
            .GroupBy(m => m.SensorId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasuredAt).First());

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
        _uow.Sensors.Update(sensor);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SensorDto>.Ok(MapSensor(sensor), "Sensor updated.");
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

        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var rowIds = rows.Values
                .Where(r => r.FarmingAreaId == areaId)
                .Select(r => r.Id)
                .ToHashSet();
            var deviceIds = sensors
                .Where(s => s.DeviceId != null && (
                    (s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                    || (s.FarmingRowId != null && rowIds.Contains(s.FarmingRowId.Value))))
                .Select(s => s.DeviceId!.Value)
                .ToHashSet();
            foreach (var id in actuators
                         .Where(c => c.RelayDeviceId != null)
                         .Select(c => c.RelayDeviceId!.Value))
                deviceIds.Add(id);
            devices = devices.Where(d =>
                d.FarmingAreaId == areaId
                || (d.FarmingRowId != null && rowIds.Contains(d.FarmingRowId.Value))
                || deviceIds.Contains(d.Id));
            sensors = sensors
                .Where(s => s.DeviceId != null && (
                    (s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                    || (s.FarmingRowId != null && rowIds.Contains(s.FarmingRowId.Value))
                    || deviceIds.Contains(s.DeviceId.Value)))
                .ToList();
        }

        if (farmingRowId is Guid rid && rid != Guid.Empty)
            devices = devices.Where(d => d.FarmingRowId == rid);

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

    public async Task<ApiResponse<DeviceDto>> CreateDeviceAsync(CreateDeviceRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceCode))
            throw AppException.BadRequest("DeviceCode is required (ESP32 id / MAC).");
        var code = req.DeviceCode.Trim();
        if (await _uow.Devices.AnyAsync(d => d.DeviceCode == code, ct))
            throw AppException.Conflict($"DeviceCode '{code}' already exists.");

        if (req.FarmingAreaId is Guid areaId && areaId != Guid.Empty)
            _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
                ?? throw AppException.NotFound("FarmingArea");

        var device = new Device
        {
            DeviceCode = code,
            Name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim(),
            DeviceType = string.IsNullOrWhiteSpace(req.DeviceType) ? "esp32" : req.DeviceType.Trim().ToLowerInvariant(),
            MacAddress = string.IsNullOrWhiteSpace(req.MacAddress) ? null : req.MacAddress.Trim(),
            IpAddress = string.IsNullOrWhiteSpace(req.IpAddress) ? null : req.IpAddress.Trim(),
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
        await _uow.SaveChangesAsync(ct);
        FarmingArea? area = null;
        if (device.FarmingAreaId is Guid aid)
            area = await _uow.FarmingAreas.GetByIdAsync(aid, ct);
        return ApiResponse<DeviceDto>.Ok(
            MapDevice(device, 0, 0, area, await RowOfAsync(device, ct)), "Controller/ESP32 registered.");
    }

    public async Task<ApiResponse<DeviceDto>> UpdateDeviceAsync(
        Guid id, UpdateDeviceRequest req, CancellationToken ct = default)
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
            device.IpAddress = string.IsNullOrWhiteSpace(req.IpAddress) ? null : req.IpAddress.Trim();
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
        if (!string.IsNullOrWhiteSpace(req.Status)
            && Enum.TryParse<DeviceStatus>(req.Status, true, out var st))
            device.Status = st;
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);
        var n = await _uow.Sensors.CountAsync(s => s.DeviceId == id, ct);
        var acts = await _uow.RasComponents.CountAsync(c => c.RelayDeviceId == id, ct);
        FarmingArea? area = null;
        if (device.FarmingAreaId is Guid aid)
            area = await _uow.FarmingAreas.GetByIdAsync(aid, ct);
        return ApiResponse<DeviceDto>.Ok(
            MapDevice(device, n, acts, area, await RowOfAsync(device, ct)), "Controller updated.");
    }

    public async Task<ApiResponse> DeleteDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        var linked = await _uow.Sensors.AnyAsync(s => s.DeviceId == id, ct);
        if (linked)
            throw AppException.Conflict("Controller still has sensors — unlink sensors first.");
        var relays = await _uow.RasComponents.AnyAsync(c => c.RelayDeviceId == id, ct);
        if (relays)
            throw AppException.Conflict("Controller still has RAS outputs — unlink relays first.");
        _uow.Devices.Remove(device);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Device deleted.");
    }

    public async Task<ApiResponse<DeviceDto>> UpdateDeviceStatusAsync(
        Guid deviceId, UpdateDeviceStatusRequest req, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct) ?? throw AppException.NotFound("Device");
        if (Enum.TryParse<DeviceStatus>(req.Status, true, out var status))
            device.Status = status;
        device.BatteryLevel = req.BatteryLevel ?? device.BatteryLevel;
        device.RssiDbm = req.RssiDbm ?? device.RssiDbm;
        if (!string.IsNullOrWhiteSpace(req.IpAddress)) device.IpAddress = req.IpAddress.Trim();
        if (!string.IsNullOrWhiteSpace(req.MacAddress)) device.MacAddress = req.MacAddress.Trim();
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
        var list = await _uow.WaterSystems.GetAllAsync(ct);
        return ApiResponse<IEnumerable<WaterSystemDto>>.Ok(list.Select(MapWs));
    }

    public async Task<ApiResponse<WaterSystemDto>> CreateWaterSystemAsync(
        CreateWaterSystemRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw AppException.BadRequest("Name is required.");
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
        Hdf5UploadRequest req, Stream fileStream, CancellationToken ct = default)
    {
        var device = await _uow.Devices.FirstOrDefaultAsync(d => d.DeviceCode == req.DeviceCode, ct)
            ?? throw AppException.NotFound("Device");

        var objectName = $"hdf5/{device.DeviceCode}/{req.ChunkStartTime:yyyyMMddHHmmss}_{req.FileName}";
        var storagePath = await _storage.UploadAsync(
            "crabsense-hdf5", objectName, fileStream, "application/octet-stream", ct);

        var upload = new Hdf5Upload
        {
            DeviceId = device.Id,
            FileName = req.FileName,
            StoragePath = storagePath,
            Checksum = req.Checksum,
            FileSizeBytes = req.FileSizeBytes,
            ChunkStartTime = req.ChunkStartTime,
            ChunkEndTime = req.ChunkEndTime,
            Status = "pending"
        };
        await _uow.Hdf5Uploads.AddAsync(upload, ct);

        device.LastSeenAt = DateTime.UtcNow;
        device.Status = DeviceStatus.Online;
        _uow.Devices.Update(device);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<string>.Ok(storagePath, "HDF5 uploaded successfully.");
    }

    private static SensorDto MapSensor(Sensor s) =>
        new(s.Id, s.WaterSystemId, s.DeviceId, s.SensorCode, s.SensorType, s.Unit,
            s.MinThreshold, s.MaxThreshold, s.IsActive, s.LastSeenAt, s.RasComponentId,
            s.FarmingRowId);

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
