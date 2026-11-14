using CrabSenseBE.Domain.Common;
namespace CrabSenseBE.Domain.Entities;

public sealed class CameraSchedule : BaseEntity
{
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "Scheduled";
    public bool Enabled { get; set; }
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    public int RetentionDays { get; set; } = 30;
    public bool AutoDeleteExpired { get; set; }
    public string? VideoQuality { get; set; }
    public int? Fps { get; set; }
    public int? MaxStorageGb { get; set; }
    public int Version { get; set; } = 1;
    public bool Deleted { get; set; }
}

public sealed class CameraScheduleAssignment : BaseEntity
{
    public Guid ScheduleId { get; set; }
    public Guid DeviceId { get; set; }
}

public sealed class CameraScheduleSlot : BaseEntity
{
    public Guid ScheduleId { get; set; }
    public int Day { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

public sealed class CameraRecordingExecution : BaseEntity
{
    public Guid ScheduleId { get; set; }
    public Guid DeviceId { get; set; }
    public int ScheduleVersion { get; set; }
    public string Status { get; set; } = "Recording";
    public DateTime StartedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? Error { get; set; }
    public Guid? MediaAssetId { get; set; }
}
public sealed class CameraRecordingAgent : BaseEntity
{
    public Guid DeviceId { get; set; }
    public Guid? KioskId { get; set; }
    public bool Enabled { get; set; }
    public int Version { get; set; } = 1;
    public DateTime? LastSeenAt { get; set; }
    public string? WorkerVersion { get; set; }
    public int PendingClips { get; set; }
}
