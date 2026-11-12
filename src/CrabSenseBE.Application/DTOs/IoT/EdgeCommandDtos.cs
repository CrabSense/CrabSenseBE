namespace CrabSenseBE.Application.DTOs.IoT;

public record EdgeCommandDto(
    Guid Id,
    string DeviceCode,
    string Command,
    string? Channel,
    string Status,
    DateTime CreatedAt,
    string? CorrelationId,
    DateTime? LeaseExpiresAt = null,
    int AttemptCount = 0,
    Guid? DeliveryAttemptId = null,
    DateTime? ExpiresAt = null);

public record EnqueueEdgeCommandRequest(
    string DeviceCode,
    string Command,
    string? Channel = null,
    string? IdempotencyKey = null);

public record EdgeCommandAckRequest(
    string Status,
    string? Message = null,
    Guid? DeliveryAttemptId = null);
