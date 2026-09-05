namespace CrabSenseBE.Application.DTOs.IoT;

// --- Sensor ---
public record SensorDto(
    Guid Id,
    Guid? WaterSystemId,
    Guid? DeviceId,
    string SensorCode,
    string SensorType,
    string? Unit,
    decimal? MinThreshold,
    decimal? MaxThreshold,
    bool IsActive,
    DateTime? LastSeenAt,
    Guid? RasComponentId = null);

public record CreateSensorRequest(
    Guid? WaterSystemId,
    Guid? DeviceId,
    string SensorCode,
    string SensorType,
    string? Unit,
    decimal? MinThreshold,
    decimal? MaxThreshold,
    Guid? RasComponentId = null);

public record UpdateSensorRequest(
    Guid? WaterSystemId,
    Guid? DeviceId,
    string? SensorType,
    string? Unit,
    decimal? MinThreshold,
    decimal? MaxThreshold,
    bool? IsActive,
    Guid? RasComponentId = null);

// --- Device (ESP32 / camera gateway) ---
public record DeviceDto(
    Guid Id,
    string DeviceCode,
    string DeviceType,
    string? FirmwareVersion,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    string Status,
    DateTime? LastSeenAt,
    int SensorCount);

public record CreateDeviceRequest(
    string DeviceCode,
    string? DeviceType = "esp32",
    string? FirmwareVersion = null,
    string? ApiKey = null);

public record UpdateDeviceRequest(
    string? DeviceType,
    string? FirmwareVersion,
    string? Status,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    string? ApiKey);

public record UpdateDeviceStatusRequest(string Status, decimal? BatteryLevel, decimal? RssiDbm);

// --- Live monitor (kiosk) ---
public record SensorLiveDto(
    Guid SensorId,
    string SensorCode,
    string SensorType,
    string? Unit,
    decimal? MinThreshold,
    decimal? MaxThreshold,
    bool IsActive,
    Guid? DeviceId,
    string? DeviceCode,
    string? DeviceStatus,
    decimal? LatestValue,
    DateTime? LatestMeasuredAt,
    DateTime? SensorLastSeenAt,
    string? Alarm);

// --- Sensor Data Ingest (ESP32 → BE) ---
public record SensorDataRequest(
    string DeviceCode,
    string SensorCode,
    decimal Value,
    string? Unit,
    DateTime MeasuredAt
);
public record SensorDataBatchRequest(IEnumerable<SensorDataRequest> Measurements);
public record SensorDataDto(Guid Id, Guid SensorId, decimal Value, string? Unit, DateTime MeasuredAt, string? Source);

// --- Water system (optional parent) ---
public record WaterSystemDto(
    Guid Id,
    Guid? FarmingAreaId,
    string Name,
    string? Type,
    bool IsActive,
    string Status = "active",
    string? FlowStatus = null,
    string? Description = null);
public record CreateWaterSystemRequest(
    string Name,
    Guid? FarmingAreaId = null,
    string? Type = null,
    string? Status = null,
    string? FlowStatus = null,
    string? Description = null);
public record UpdateWaterSystemRequest(
    string? Name,
    Guid? FarmingAreaId,
    string? Type,
    bool? IsActive,
    string? Status = null,
    string? FlowStatus = null,
    string? Description = null);

// --- HDF5 Upload ---
public record Hdf5UploadRequest(
    string DeviceCode,
    string FileName,
    string Checksum,
    long FileSizeBytes,
    DateTime ChunkStartTime,
    DateTime ChunkEndTime
);
