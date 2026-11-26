using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Configured local UART pair. Scope comes from the authenticated kiosk, never an edge-supplied area.</summary>
public sealed class WaterBenchStation : BaseEntity
{
    public Guid KioskId { get; set; }
    public string MasterHardwareId { get; set; } = "";
    public string SlaveHardwareId { get; set; } = "";
    public DateTime? RevokedAt { get; set; }
}

/// <summary>Immutable received evidence, not a concentration, hardware command or verified physical result.</summary>
public sealed class WaterBenchEvidence : BaseEntity
{
    public Guid StationId { get; set; }
    public Guid EventId { get; set; }
    public Guid ConnectionId { get; set; }
    public string Kind { get; set; } = "";
    public string? BootId { get; set; }
    public string? FirmwareBatchId { get; set; }
    public string? FirmwareSessionId { get; set; }
    public Guid? RequestId { get; set; }
    public int? CaptureId { get; set; }
    public string? Analyte { get; set; }
    public DateTime? KioskReceivedAt { get; set; }
    public string PayloadSha256 { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
}
