namespace CrabSenseBE.Application.DTOs.IoT;

// --- Sensor ---
public record SensorDto(Guid Id, Guid? WaterSystemId, string SensorCode, string SensorType, string? Unit, bool IsActive);
public record CreateSensorRequest(Guid? WaterSystemId, string SensorCode, string SensorType, string? Unit, decimal? MinThreshold, decimal? MaxThreshold);

// --- Device ---
public record DeviceDto(Guid Id, string DeviceCode, string? FirmwareVersion, decimal? BatteryLevel, decimal? RssiDbm, string Status, DateTime? LastSeenAt);
public record UpdateDeviceStatusRequest(string Status, decimal? BatteryLevel, decimal? RssiDbm);

// --- Sensor Data Ingest ---
public record SensorDataRequest(
    string DeviceCode,
    string SensorCode,
    decimal Value,
    string? Unit,
    DateTime MeasuredAt
);
public record SensorDataBatchRequest(IEnumerable<SensorDataRequest> Measurements);
public record SensorDataDto(Guid Id, Guid SensorId, decimal Value, string? Unit, DateTime MeasuredAt, string? Source);

// --- HDF5 Upload ---
public record Hdf5UploadRequest(
    string DeviceCode,
    string FileName,
    string Checksum,
    long FileSizeBytes,
    DateTime ChunkStartTime,
    DateTime ChunkEndTime
);
