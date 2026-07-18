using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Services;

namespace CrabSenseBE.Application.Services;

public class IotService : IIotService
{
    private readonly IUnitOfWork _uow;
    private readonly IStorageService _storage;

    public IotService(IUnitOfWork uow, IStorageService storage)
    {
        _uow = uow;
        _storage = storage;
    }

    public async Task<ApiResponse> IngestSensorDataAsync(SensorDataRequest req, CancellationToken ct = default)
    {
        var sensor = await _uow.Sensors.FirstOrDefaultAsync(
            s => s.SensorCode == req.SensorCode, ct)
            ?? throw AppException.NotFound("Sensor");

        var measurement = new WaterMeasurement
        {
            SensorId = sensor.Id,
            Value = req.Value,
            Unit = req.Unit,
            MeasuredAt = req.MeasuredAt,
            Source = "realtime"
        };
        await _uow.WaterMeasurements.AddAsync(measurement, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Sensor data ingested.");
    }

    public async Task<ApiResponse> IngestSensorDataBatchAsync(SensorDataBatchRequest req, CancellationToken ct = default)
    {
        foreach (var item in req.Measurements)
            await IngestSensorDataAsync(item, ct);
        return ApiResponse.Ok($"Batch of {req.Measurements.Count()} records ingested.");
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
                       .Skip((page - 1) * pageSize).Take(pageSize)
                       .Select(m => new SensorDataDto(m.Id, m.SensorId, m.Value, m.Unit, m.MeasuredAt, m.Source));

        return ApiResponse<PagedResult<SensorDataDto>>.Ok(new PagedResult<SensorDataDto>
        {
            Items = items, TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<IEnumerable<SensorDto>>> GetSensorsAsync(CancellationToken ct = default)
    {
        var sensors = await _uow.Sensors.GetAllAsync(ct);
        return ApiResponse<IEnumerable<SensorDto>>.Ok(sensors.Select(s =>
            new SensorDto(s.Id, s.WaterSystemId, s.SensorCode, s.SensorType, s.Unit, s.IsActive)));
    }

    public async Task<ApiResponse<SensorDto>> CreateSensorAsync(CreateSensorRequest req, CancellationToken ct = default)
    {
        var sensor = new Sensor
        {
            WaterSystemId = req.WaterSystemId,
            SensorCode = req.SensorCode,
            SensorType = req.SensorType,
            Unit = req.Unit,
            MinThreshold = req.MinThreshold,
            MaxThreshold = req.MaxThreshold
        };
        await _uow.Sensors.AddAsync(sensor, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<SensorDto>.Ok(
            new SensorDto(sensor.Id, sensor.WaterSystemId, sensor.SensorCode, sensor.SensorType, sensor.Unit, sensor.IsActive), "Created.");
    }

    public async Task<ApiResponse<IEnumerable<DeviceDto>>> GetDevicesAsync(CancellationToken ct = default)
    {
        var devices = await _uow.Devices.GetAllAsync(ct);
        return ApiResponse<IEnumerable<DeviceDto>>.Ok(devices.Select(MapDevice));
    }

    public async Task<ApiResponse<DeviceDto>> UpdateDeviceStatusAsync(Guid deviceId, UpdateDeviceStatusRequest req, CancellationToken ct = default)
    {
        var device = await _uow.Devices.GetByIdAsync(deviceId, ct) ?? throw AppException.NotFound("Device");
        if (Enum.TryParse<Domain.Enums.DeviceStatus>(req.Status, true, out var status))
            device.Status = status;
        device.BatteryLevel = req.BatteryLevel ?? device.BatteryLevel;
        device.RssiDbm = req.RssiDbm ?? device.RssiDbm;
        device.LastSeenAt = DateTime.UtcNow;
        _uow.Devices.Update(device);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<DeviceDto>.Ok(MapDevice(device));
    }

    public async Task<ApiResponse<string>> ProcessHdf5UploadAsync(
        Hdf5UploadRequest req, Stream fileStream, CancellationToken ct = default)
    {
        var device = await _uow.Devices.FirstOrDefaultAsync(
            d => d.DeviceCode == req.DeviceCode, ct)
            ?? throw AppException.NotFound("Device");

        var objectName = $"hdf5/{device.DeviceCode}/{req.ChunkStartTime:yyyyMMddHHmmss}_{req.FileName}";
        var storagePath = await _storage.UploadAsync("crabsense-hdf5", objectName, fileStream, "application/octet-stream", ct);

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
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<string>.Ok(storagePath, "HDF5 uploaded successfully.");
    }

    private static DeviceDto MapDevice(Device d) =>
        new(d.Id, d.DeviceCode, d.FirmwareVersion, d.BatteryLevel, d.RssiDbm, d.Status.ToString(), d.LastSeenAt);
}
