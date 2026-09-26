namespace CrabSenseBE.Application.DTOs.IoT;

public record EdgeCommandDto(
    Guid Id,
    string DeviceCode,
    string Command,
    string? Channel,
    string Status,
    DateTime CreatedAt,
    string? CorrelationId);

public record EnqueueEdgeCommandRequest(
    string DeviceCode,
    string Command,
    string? Channel = null);

public record EdgeCommandAckRequest(
    string Status,
    string? Message = null);
