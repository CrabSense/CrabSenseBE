using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

// Inventory is a report, not permission to energize an output.
public sealed class ControllerPortInventory
{
    public Guid DeviceId { get; set; }
    public long Revision { get; set; }
    public string ManifestHash { get; set; } = "";
    public DateTime ReportedAt { get; set; }
}

public sealed class ControllerPort
{
    public Guid DeviceId { get; set; }
    public string PortId { get; set; } = "";
    public string? Channel { get; set; }
    public int? Gpio { get; set; }
    public string Kind { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Discovery { get; set; } = "";
    public string? HardwareUid { get; set; }
    public bool Present { get; set; }
    public bool RemoteControl { get; set; }
    public DateTime LastSeenAt { get; set; }
    public bool? ObservedOn { get; set; }
    public DateTime? LastObservedAt { get; set; }
}
