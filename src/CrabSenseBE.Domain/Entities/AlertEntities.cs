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

    /// <summary>Kênh: in-app | push | telegram | zalo | email</summary>
    public string Channel { get; set; } = "in-app";
    public bool IsRead { get; set; } = false;
    public DateTime? ReadAt { get; set; }

    public AppUser? User { get; set; }
    public Alert? Alert { get; set; }
}

/// <summary>
/// Cấu hình kênh gửi thông báo (Telegram bot, Zalo OA, …).
/// Tương ứng bảng DB: notification_channel.
/// </summary>
public class NotificationChannel : BaseEntity
{
    /// <summary>Mã kênh: in_app, telegram, zalo_oa, email, push</summary>
    public string ChannelCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;

    /// <summary>JSON cấu hình (bot_token, chat_id, oa_id, …).</summary>
    public string? ConfigJson { get; set; }
}

/// <summary>
/// Log từng lần gửi thông báo (pending/sent/failed).
/// Tương ứng bảng DB: notification_delivery.
/// </summary>
public class NotificationDelivery : BaseEntity
{
    public Guid NotificationId { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string? Recipient { get; set; }
    public string Status { get; set; } = "pending"; // pending | sent | failed
    public DateTime? SentAt { get; set; }
    public string? ErrorMessage { get; set; }

    public Notification? Notification { get; set; }
}
