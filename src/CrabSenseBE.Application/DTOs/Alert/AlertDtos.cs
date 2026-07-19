namespace CrabSenseBE.Application.DTOs.Alert;

// --- Ngưỡng cảnh báo ---
public record AlertThresholdDto(
    Guid Id, string SensorType, decimal MinValue, decimal MaxValue, string Severity, bool IsActive);

public record CreateAlertThresholdRequest(
    string SensorType, decimal MinValue, decimal MaxValue, string Severity = "Warning");

public record UpdateAlertThresholdRequest(
    decimal MinValue, decimal MaxValue, string Severity, bool IsActive);

// --- Cảnh báo ---
public record AlertDto(
    Guid Id, Guid? SensorId, string Message, string Severity, string Status,
    decimal? TriggerValue, DateTime CreatedAt, DateTime? AcknowledgedAt);

public record AcknowledgeAlertRequest(Guid? UserId);

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
