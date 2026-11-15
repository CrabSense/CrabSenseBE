using CrabSenseBE.Domain.Common;
namespace CrabSenseBE.Domain.Entities;
public sealed class DeviceConnectionEvent:BaseEntity
{
    public string SourceType {get;set;}="";
    public Guid SourceId {get;set;}
    public Guid? FarmingAreaId {get;set;}
    public string? PreviousStatus {get;set;}
    public string NewStatus {get;set;}="";
    public string ReasonCode {get;set;}="";
    public DateTime OccurredAt {get;set;}
    public DateTime? LastSeenAt {get;set;}
    public DateTime? PreviousObservedAt {get;set;}
}
public sealed class DeviceMonitoringState:BaseEntity
{
    public string SourceType {get;set;}="";
    public Guid SourceId {get;set;}
    public Guid? FarmingAreaId {get;set;}
    public string Status {get;set;}="";
    public DateTime ObservedAt {get;set;}
}

public sealed class DeviceMonitoringPolicy:BaseEntity
{
    public string SourceType {get;set;}="";
    public int HeartbeatTimeoutSeconds {get;set;}
    public int Version {get;set;}=1;
    public Guid UpdatedBy {get;set;}
}
