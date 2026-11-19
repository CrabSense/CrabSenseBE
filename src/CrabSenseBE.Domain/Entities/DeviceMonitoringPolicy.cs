using CrabSenseBE.Domain.Common;
namespace CrabSenseBE.Domain.Entities;

/// <summary>Monitor-only heartbeat threshold. Guid.Empty means the default for the source type.</summary>
public sealed class DeviceMonitoringPolicy : BaseEntity
{
    public string SourceType { get; set; } = "";
    public Guid SourceId { get; set; }
    public int TimeoutSeconds { get; set; }
    public int Version { get; set; }
    public bool IsInherited { get; set; }
    public Guid UpdatedBy { get; set; }
}
