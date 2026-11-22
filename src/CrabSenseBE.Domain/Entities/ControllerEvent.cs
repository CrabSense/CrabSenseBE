using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Authenticated Edge V2 hardware history; heartbeat events are sampled, not every packet.</summary>
public sealed class ControllerEvent : BaseEntity
{
    public Guid DeviceId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int InventoryRevision { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
}
