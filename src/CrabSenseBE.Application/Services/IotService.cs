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
                    LastSeenAt = DateTime.UtcNow
                };
                await _uow.Devices.AddAsync(device, ct);
            }
            else
            {
                device.LastSeenAt = DateTime.UtcNow;
                device.Status = DeviceStatus.Online;
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
            .Skip((page - 1) * Math.Max(1, pageSize)).Take(Math.Clamp(pageSize, 1, 500))
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

        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            sensors = sensors.Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value));
        }

        var devices = (await _uow.Devices.GetAllAsync(ct)).ToDictionary(d => d.Id);
        var allMeas = await _uow.WaterMeasurements.GetAllAsync(ct);
        var latestBySensor = allMeas
            .GroupBy(m => m.SensorId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasuredAt).First());

        var list = new List<SensorLiveDto>();
        foreach (var s in sensors.OrderBy(s => s.SensorCode))
        {
            devices.TryGetValue(s.DeviceId ?? Guid.Empty, out var dev);
            latestBySensor.TryGetValue(s.Id, out var latest);
            string? alarm = null;
            if (latest is not null)
            {
                if (s.MinThreshold.HasValue && latest.Value < s.MinThreshold.Value)
                    alarm = "below_min";
                else if (s.MaxThreshold.HasValue && latest.Value > s.MaxThreshold.Value)
                    alarm = "above_max";
            }
            list.Add(new SensorLiveDto(
                s.Id, s.SensorCode, s.SensorType, s.Unit,
                s.MinThreshold, s.MaxThreshold, s.IsActive,
                s.DeviceId, dev?.DeviceCode, dev?.Status.ToString(),
                latest?.Value, latest?.MeasuredAt, s.LastSeenAt, alarm));
        }
        return ApiResponse<IEnumerable<SensorLiveDto>>.Ok(list);
    }

    // ─── Sensors CRUD ───────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<SensorDto>>> GetSensorsAsync(
        Guid? deviceId = null, CancellationToken ct = default)
    {
        var sensors = (await _uow.Sensors.GetAllAsync(ct)).AsEnumerable();
        if (deviceId.HasValue)
            sensors = sensors.Where(s => s.DeviceId == deviceId.Value);
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

        var sensor = new Sensor
        {
            WaterSystemId = req.WaterSystemId,
            DeviceId = req.DeviceId,
            RasComponentId = req.RasComponentId,
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
        CancellationToken ct = default)
    {
        var devices = (await _uow.Devices.GetAllAsync(ct)).AsEnumerable();
        var sensors = (await _uow.Sensors.GetAllAsync(ct)).ToList();

        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var deviceIds = sensors
                .Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value) && s.DeviceId != null)
                .Select(s => s.DeviceId!.Value)
                .ToHashSet();
            devices = devices.Where(d => deviceIds.Contains(d.Id));
            sensors = sensors
                .Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                .ToList();
        }

        var countBy = sensors.Where(s => s.DeviceId.HasValue)
            .GroupBy(s => s.DeviceId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return ApiResponse<IEnumerable<DeviceDto>>.Ok(
            devices.Select(d => MapDevice(d, countBy.GetValueOrDefault(d.Id))));
    }

    public async Task<ApiResponse<DeviceDto>> GetDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        var n = await _uow.Sensors.CountAsync(s => s.DeviceId == id, ct);
        return ApiResponse<DeviceDto>.Ok(MapDevice(d, n));
    }

    public async Task<ApiResponse<DeviceDto>> CreateDeviceAsync(CreateDeviceRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.DeviceCode))
            throw AppException.BadRequest("DeviceCode is required (ESP32 id / MAC).");
        var code = req.DeviceCode.Trim();
        if (await _uow.Devices.AnyAsync(d => d.DeviceCode == code, ct))
            throw AppException.Conflict($"DeviceCode '{code}' already exists.");

        var device = new Device
        {
            DeviceCode = code,
            DeviceType = string.IsNullOrWhiteSpace(req.DeviceType) ? "esp32" : req.DeviceType.Trim().ToLowerInvariant(),
            FirmwareVersion = req.FirmwareVersion,
            ApiKey = req.ApiKey,
            Status = DeviceStatus.Offline
        };
        await _uow.Devices.AddAsync(device, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<DeviceDto>.Ok(MapDevice(device, 0), "ESP32/device registered.");
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
        if (!string.IsNullOrWhiteSpace(req.Status)
            && Enum.TryParse<DeviceStatus>(req.Status, true, out var st))
            device.Status = st;
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);
        var n = await _uow.Sensors.CountAsync(s => s.DeviceId == id, ct);
        return ApiResponse<DeviceDto>.Ok(MapDevice(device, n), "Device updated.");
    }

    public async Task<ApiResponse> DeleteDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(id, ct) ?? throw AppException.NotFound("Device");
        var linked = await _uow.Sensors.AnyAsync(s => s.DeviceId == id, ct);
        if (linked)
            throw AppException.Conflict("Device still has sensors — unlink/delete sensors first.");
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
        device.LastSeenAt = DateTime.UtcNow;
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);
        var n = await _uow.Sensors.CountAsync(s => s.DeviceId == deviceId, ct);
        return ApiResponse<DeviceDto>.Ok(MapDevice(device, n));
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
            s.MinThreshold, s.MaxThreshold, s.IsActive, s.LastSeenAt, s.RasComponentId);

    private static DeviceDto MapDevice(Device d, int sensorCount) =>
        new(d.Id, d.DeviceCode, string.IsNullOrWhiteSpace(d.DeviceType) ? "esp32" : d.DeviceType, d.FirmwareVersion, d.BatteryLevel, d.RssiDbm,
            d.Status.ToString(), d.LastSeenAt, sensorCount);

    private static WaterSystemDto MapWs(WaterSystem w) =>
        new(w.Id, w.FarmingAreaId, w.Name, w.Type, w.IsActive,
            string.IsNullOrWhiteSpace(w.Status) ? "active" : w.Status, w.FlowStatus, w.Description);
}
