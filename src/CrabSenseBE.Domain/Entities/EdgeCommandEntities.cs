using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Command queued by Cloud for a Kiosk/ESP device.</summary>
public class EdgeCommand : BaseEntity
{
    public string DeviceCode { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public string Status { get; set; } = "pending"; // pending, delivered, acknowledged, failed, timedout, superseded
    public DateTime? DeliveredAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public string? LeaseHolder { get; set; }
    public Guid? DeliveryAttemptId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ResultMessage { get; set; }
    public string? CorrelationId { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public string RequestSource { get; set; } = "manual";
    public uint ConcurrencyVersion { get; set; }
    public long CommandSequence { get; set; }
    public DateTime? SupersededAt { get; set; }
    public Guid? SupersededByCommandId { get; set; }
}
