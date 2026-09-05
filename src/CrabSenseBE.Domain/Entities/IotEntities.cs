using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Hệ thống nước RAS (Recirculating Aquaculture System) — tương đương RASSystem.</summary>
public class WaterSystem : BaseEntity
{
    public Guid? FarmingAreaId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>active | maintenance | stopped</summary>
    public string Status { get; set; } = "active";
    /// <summary>ok | low | stopped — trạng thái tuần hoàn.</summary>
    public string? FlowStatus { get; set; }
    public string? Description { get; set; }

    // Navigation
    public FarmingArea? FarmingArea { get; set; }
    public ICollection<Sensor> Sensors { get; set; } = new List<Sensor>();
    public ICollection<WaterMeasurement> WaterMeasurements { get; set; } = new List<WaterMeasurement>();
    public ICollection<RasComponent> Components { get; set; } = new List<RasComponent>();
    public ICollection<WaterFlow> Flows { get; set; } = new List<WaterFlow>();
}

/// <summary>Cảm biến IoT — MOD-IOT</summary>
public class Sensor : BaseEntity
{
    public Guid? WaterSystemId { get; set; }
    public Guid? DeviceId { get; set; }
    /// <summary>Gắn cảm biến vào 1 thành phần RAS (drum, skimmer…); null = cả hệ thống.</summary>
    public Guid? RasComponentId { get; set; }
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
    public RasComponent? RasComponent { get; set; }
    public ICollection<WaterMeasurement> Measurements { get; set; } = new List<WaterMeasurement>();
}

/// <summary>
/// Một mắt xích trong sơ đồ tuần hoàn (Drum Filter, Skimmer, bể vi sinh…).
/// Dùng Type/Code — không tách bảng theo từng loại bể.
/// </summary>
public class RasComponent : BaseEntity
{
    public Guid WaterSystemId { get; set; }
    /// <summary>Mã UI / icon: drum, skimmer, bio, settling…</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>FILTER | DRAIN_TANK | BIOFILTER | SKIMMER | CORAL_TANK | SETTLING_TANK | CULTURE | PUMP | …</summary>
    public string Type { get; set; } = "FILTER";
    public string Status { get; set; } = "active";
    /// <summary>Thứ tự trên sơ đồ (0 = đầu dòng).</summary>
    public int Position { get; set; }
    public decimal? Capacity { get; set; }
    public string? Description { get; set; }
    public string? NodeType { get; set; } = "equipment";
    public string? IconKey { get; set; }
    public Guid? RelayDeviceId { get; set; }
    public string? RelayChannel { get; set; }
    public string? ParamDefaultsJson { get; set; }
    public bool HasRelay { get; set; }
    public bool IsOn { get; set; }
    public string? ControlMode { get; set; }

    public WaterSystem? WaterSystem { get; set; }
    public Device? RelayDevice { get; set; }
    public ICollection<Sensor> Sensors { get; set; } = new List<Sensor>();
    public ICollection<WaterFlow> OutgoingFlows { get; set; } = new List<WaterFlow>();
    public ICollection<WaterFlow> IncomingFlows { get; set; } = new List<WaterFlow>();
}

/// <summary>Luồng nước giữa hai RASComponent.</summary>
public class WaterFlow : BaseEntity
{
    public Guid WaterSystemId { get; set; }
    public Guid FromComponentId { get; set; }
    public Guid ToComponentId { get; set; }
    /// <summary>L/h</summary>
    public decimal? FlowRate { get; set; }
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }

    public WaterSystem? WaterSystem { get; set; }
    public RasComponent? FromComponent { get; set; }
    public RasComponent? ToComponent { get; set; }
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
