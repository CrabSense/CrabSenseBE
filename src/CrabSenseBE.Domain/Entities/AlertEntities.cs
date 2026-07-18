using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Ngưỡng cảnh báo theo loại cảm biến</summary>
public class AlertThreshold : BaseEntity
{
    public string SensorType { get; set; } = string.Empty;
    public decimal MinValue { get; set; }
    public decimal MaxValue { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public bool IsActive { get; set; } = true;
}

/// <summary>Cảnh báo phát sinh — MOD-SYS</summary>
public class Alert : BaseEntity
{
    public Guid? SensorId { get; set; }
    public Guid? AlertThresholdId { get; set; }
    public string Message { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public AlertStatus Status { get; set; } = AlertStatus.Active;
    public decimal? TriggerValue { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedBy { get; set; }

    // Navigation
    public Sensor? Sensor { get; set; }
    public AlertThreshold? AlertThreshold { get; set; }
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}

/// <summary>Thông báo đến người dùng — MOD-SYS</summary>
public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid? AlertId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Channel { get; set; } = "in-app"; // in-app, email, push
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    // Navigation
    public AppUser? User { get; set; }
    public Alert? Alert { get; set; }
}
