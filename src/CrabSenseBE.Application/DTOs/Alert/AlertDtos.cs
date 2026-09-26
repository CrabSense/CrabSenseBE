namespace CrabSenseBE.Application.DTOs.Alert;

// --- Ngưỡng cảnh báo ---
public record AlertThresholdDto(
    Guid Id, string SensorType, decimal MinValue, decimal MaxValue, string Severity, bool IsActive);

public record CreateAlertThresholdRequest(
    string SensorType, decimal MinValue, decimal MaxValue, string Severity = "Warning");

public record UpdateAlertThresholdRequest(
    decimal MinValue, decimal MaxValue, string Severity, bool IsActive);

// --- Cảnh báo (enriched for Mobile Alerts Command Center) ---
public record AlertDto(
    Guid Id,
    Guid? SensorId,
    string Message,
    string Severity,
    string Status,
    decimal? TriggerValue,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt,
    string Title,
    string Category,
    string? SensorCode,
    string? SensorType,
    string? Unit,
    Guid? FarmingAreaId,
    string? FarmingAreaName,
    string? LocationLabel,
    decimal? ThresholdMin,
    decimal? ThresholdMax,
    int PriorityScore,
    string PriorityExplanation,
    string SlaLabel,
    string? AiRecommendation,
    int? AiConfidence,
    Guid? AcknowledgedBy,
    string? Description = null,
    string? SourceLabel = null,
    string? Kind = null,
    string? DeviceCode = null,
    string? AreaCode = null,
    DateTime? ProcessingStartedAt = null,
    DateTime? ResolvedAt = null,
    string? ResolutionReason = null,
    string? ResolutionAction = null,
    string? ResolutionNote = null,
    int OccurrenceCount = 1,
    DateTime? LastOccurredAt = null,
    string? IncidentId = null);

public record AcknowledgeAlertRequest(Guid? UserId);

public record ResolveAlertRequest(
    string? Reason = null,
    string? Action = null,
    string? Note = null,
    bool? DeviceRecovered = null,
    Guid? UserId = null);

public record StartProcessingRequest(Guid? UserId = null);

public record AlertUnreadCountDto(int Count, int UnreadCount);

// --- Thông báo ---
public record NotificationDto(
    Guid Id, Guid UserId, Guid? AlertId, string Title, string Body,
    string Channel, bool IsRead, DateTime CreatedAt);

public record NotificationChannelDto(
    Guid Id, string ChannelCode, string DisplayName, bool IsEnabled, string? ConfigJson);

public record CreateNotificationChannelRequest(
    string ChannelCode, string DisplayName, bool IsEnabled = false, string? ConfigJson = null);

public record UpdateNotificationChannelRequest(
    bool IsEnabled, string? ConfigJson = null, string? DisplayName = null);

public record TestNotificationChannelRequest(string? Title = null, string? Body = null);

public record RegisterPushTokenRequest(
    string Token, string Platform = "android", string? DeviceId = null);

public record PushTokenDto(
    Guid Id, Guid UserId, string Token, string Platform, string? DeviceId, bool IsActive, DateTime LastSeenAt);

public record NotificationChannelSettingDto(bool Enabled, string? ConfigJson);

public record NotificationSettingsDto(
    bool PushEnabled,
    NotificationChannelSettingDto Telegram,
    NotificationChannelSettingDto Zalo);

public record UpdateNotificationSettingsRequest(
    bool? PushEnabled = null,
    NotificationChannelSettingDto? Telegram = null,
    NotificationChannelSettingDto? Zalo = null);
