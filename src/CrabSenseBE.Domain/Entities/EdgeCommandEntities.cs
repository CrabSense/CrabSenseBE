using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Command queued by Cloud for a Kiosk/ESP device.</summary>
public class EdgeCommand : BaseEntity
{
    public string DeviceCode { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? Channel { get; set; }
    public string Status { get; set; } = "pending"; // pending, delivered, acknowledged, failed
    public DateTime? DeliveredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? ResultMessage { get; set; }
    public string? CorrelationId { get; set; }
}
