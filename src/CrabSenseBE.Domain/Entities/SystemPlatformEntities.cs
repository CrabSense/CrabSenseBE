using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

public class CameraRecordingSetting : BaseEntity
{
    public Guid DeviceId { get; set; }
    public string Mode { get; set; } = "Disabled";
    public int RetentionDays { get; set; } = 30;
    public string? VideoQuality { get; set; }
    public int? Fps { get; set; }
    public int? MaxStorageGb { get; set; }
    public string? WeeklyScheduleJson { get; set; }

    public Device? Device { get; set; }
}

public class AiManagedService : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public string Status { get; set; } = "Stopped";
    public string? DeviceLabel { get; set; }
    public decimal ConfidenceThreshold { get; set; } = 0.75m;
    public decimal FrameSamplingFps { get; set; } = 2m;
    public int BatchSize { get; set; } = 4;
    public string DetectionClassesJson { get; set; } = "[]";
    public DateTime? StartedAt { get; set; }
}

public class BackupScheduleSetting : BaseEntity
{
    public bool Enabled { get; set; }
    public string Frequency { get; set; } = "Daily";
    public string BackupTime { get; set; } = "02:00";
    public int RetentionDays { get; set; } = 30;
}

public class SystemDeployment : BaseEntity
{
    public string Component { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Environment { get; set; } = "Production";
    public string Status { get; set; } = "Requested";
    public string? DeployedBy { get; set; }
    public Guid? DeployedByUserId { get; set; }
    public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
}

public class ServiceApiKey : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class SecurityPolicySetting : BaseEntity
{
    public int SessionDays { get; set; } = 30;
    public int FailedLoginAlertThreshold { get; set; } = 5;
    public bool RequireConfirmRestore { get; set; } = true;
}
