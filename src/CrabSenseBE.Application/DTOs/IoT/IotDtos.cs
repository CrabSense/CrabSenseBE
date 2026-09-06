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

// --- Device = Controller (một ESP32). Sensor + relay/actuator thuộc Device. ---
public record DeviceDto(
    Guid Id,
    string DeviceCode,
    string DeviceType,
    string? FirmwareVersion,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    string Status,
    DateTime? LastSeenAt,
    int SensorCount,
    string? Name = null,
    string? MacAddress = null,
    string? IpAddress = null,
    Guid? FarmingAreaId = null,
    string? AreaName = null,
    string? AreaCode = null,
    int ActuatorCount = 0);

public record DeviceActuatorDto(
    Guid Id,
    string Code,
    string Name,
    string Type,
    bool IsOn,
    string? ControlMode,
    string? RelayChannel);

public record DeviceDetailDto(
    Guid Id,
    string DeviceCode,
    string DeviceType,
    string? FirmwareVersion,
    string Status,
    DateTime? LastSeenAt,
    int SensorCount,
    int ActuatorCount,
    string? Name,
    string? MacAddress,
    string? IpAddress,
    Guid? FarmingAreaId,
    string? AreaName,
    string? AreaCode,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    IReadOnlyList<SensorDto> Sensors,
    IReadOnlyList<DeviceActuatorDto> Actuators);

public record CreateDeviceRequest(
    string DeviceCode,
    string? DeviceType = "esp32",
    string? FirmwareVersion = null,
    string? ApiKey = null,
    string? Name = null,
    string? MacAddress = null,
    string? IpAddress = null,
    Guid? FarmingAreaId = null);

public record UpdateDeviceRequest(
    string? DeviceType,
    string? FirmwareVersion,
    string? Status,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    string? ApiKey,
    string? Name = null,
    string? MacAddress = null,
    string? IpAddress = null,
    Guid? FarmingAreaId = null);

public record UpdateDeviceStatusRequest(
    string Status,
    decimal? BatteryLevel,
    decimal? RssiDbm,
    string? IpAddress = null,
    string? MacAddress = null);

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
    string? Alarm,
    string? LocationName = null,
    string? LocationType = null);

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
