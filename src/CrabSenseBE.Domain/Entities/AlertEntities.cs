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
    public DateTime? ProcessingStartedAt { get; set; }
    public Guid? ProcessingBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionReason { get; set; }
    public string? ResolutionAction { get; set; }
    public string? ResolutionNote { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTime? LastOccurredAt { get; set; }
    public string? IncidentId { get; set; }

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

    /// <summary>Loại thông báo — khớp NotificationRule.TypeCode. Null với dữ liệu cũ.</summary>
    public string? TypeCode { get; set; }

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

/// <summary>
/// Quy tắc thông báo theo loại: bật/tắt, người nhận, kênh, mẫu nội dung.
/// Admin cấu hình qua /api/notification-rules.
/// </summary>
public class NotificationRule : BaseEntity
{
    /// <summary>Mã loại — xem NotificationTypeCodes.</summary>
    public string TypeCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;

    /// <summary>Ngưỡng tối thiểu (Info/Warning/Critical). Null = mọi mức.</summary>
    public string? MinSeverity { get; set; }

    /// <summary>Role nhận, phân tách bằng dấu phẩy. Rỗng = không gửi.</summary>
    public string RecipientRoles { get; set; } = "FarmAdministrator,FarmManager,FarmStaff";

    /// <summary>Chỉ FarmAdministrator sở hữu khu liên quan mới nhận.</summary>
    public bool ScopeByArea { get; set; }

    /// <summary>Kênh gửi, phân tách bằng dấu phẩy: in_app, push, telegram, zalo_oa, email.</summary>
    public string ChannelCodes { get; set; } = "in_app,push";

    /// <summary>Mẫu tiêu đề, hỗ trợ {message} {severity} {sensor} {value} {code} {name}…</summary>
    public string? TitleTemplate { get; set; }
    public string? BodyTemplate { get; set; }
}

/// <summary>FCM device token đăng ký từ Mobile App.</summary>
public class UserPushToken : BaseEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string? DeviceId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}
