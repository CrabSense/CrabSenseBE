using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Hệ thống nước RAS (Recirculating Aquaculture System)</summary>
public class WaterSystem : BaseEntity
{
    public Guid? FarmingAreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public FarmingArea? FarmingArea { get; set; }
    public ICollection<Sensor> Sensors { get; set; } = new List<Sensor>();
    public ICollection<WaterMeasurement> WaterMeasurements { get; set; } = new List<WaterMeasurement>();
}

/// <summary>Cảm biến IoT — MOD-IOT</summary>
public class Sensor : BaseEntity
{
    public Guid? WaterSystemId { get; set; }
    public Guid? DeviceId { get; set; }
    public string SensorCode { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty; // pH, DO, Temperature, Salinity, etc.
    public string? Unit { get; set; }
    public decimal? MinThreshold { get; set; }
    public decimal? MaxThreshold { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Lần cuối nhận dữ liệu — dùng phát hiện mất kết nối cảm biến.</summary>
    public DateTime? LastSeenAt { get; set; }

    // Navigation
    public WaterSystem? WaterSystem { get; set; }
    public Device? Device { get; set; }
    public ICollection<WaterMeasurement> Measurements { get; set; } = new List<WaterMeasurement>();
}

/// <summary>Thiết bị gateway/edge — MOD-IOT</summary>
public class Device : BaseEntity
{
    public string DeviceCode { get; set; } = string.Empty;

    /// <summary>esp32 | camera | gateway | other</summary>
    public string DeviceType { get; set; } = "esp32";

    public string? FirmwareVersion { get; set; }
    public decimal? BatteryLevel { get; set; }
    public decimal? RssiDbm { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.Offline;
    public DateTime? LastSeenAt { get; set; }
    public string? ApiKey { get; set; } // API key for edge sync

    // Navigation
    public ICollection<Sensor> Sensors { get; set; } = new List<Sensor>();
}

/// <summary>Dữ liệu đo nước từ IoT — MOD-IOT</summary>
public class WaterMeasurement : BaseEntity
{
    public Guid SensorId { get; set; }
    public Guid? WaterSystemId { get; set; }
    public decimal Value { get; set; }
    public string? Unit { get; set; }
    public DateTime MeasuredAt { get; set; }
    public string? Source { get; set; } // realtime, hdf5

    // Navigation
    public Sensor? Sensor { get; set; }
    public WaterSystem? WaterSystem { get; set; }
}

/// <summary>Upload file HDF5 từ Kiosk edge — Edge Sync</summary>
public class Hdf5Upload : BaseEntity
{
    public Guid? DeviceId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty; // MinIO object key
    public string? Checksum { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime ChunkStartTime { get; set; }
    public DateTime ChunkEndTime { get; set; }
    public string Status { get; set; } = "pending"; // pending, processed, error

    // Navigation
    public Device? Device { get; set; }
}
