using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Infrastructure.Services;

public sealed record MonitoringRow(Guid Id,string Type,string Code,string? Name,Guid? FarmingAreaId,Guid? FarmingRowId,
    string? AreaName,string? RowName,string ConnectionStatus,string HealthStatus,string? ProvisioningStatus,
    DateTime? LastSeenAt,int TimeoutSeconds,string? IpAddress=null,string? FirmwareVersion=null,Guid? KioskId=null,
    string? KioskCode=null,string? KioskStatus=null,Guid? ControllerId=null,string? ControllerCode=null,string? PortCode=null,
    decimal? LatestValue=null,string? Unit=null,DateTime? LatestMeasuredAt=null,DateTime? LastReceivedAt=null,string? DataQuality=null,
    string? AgentStatus=null,DateTime? AgentLastSeenAt=null,int? PendingClips=null,string? RecordingStatus=null,
    string? ScheduleName=null,int PortCount=0,int PendingCommands=0,int ActiveAlertCount=0,string? ReasonCode=null);

public sealed class DeviceMonitoringReader(AppDbContext db)
{
    public static bool IsOpen(Alert a)=>a.Status is AlertStatus.Active or AlertStatus.Acknowledged or AlertStatus.InProgress;
    public static IQueryable<Alert> TechnicalAlerts(AppDbContext db)=>db.Alerts.AsNoTracking().Where(a=>
        (a.SourceType=="Sensor"&&(a.IncidentType=="sensor_disconnect"||a.IncidentType=="sensor_read_error"))||
        (a.SourceType=="Device"&&(a.IncidentType=="device_disconnect"||a.IncidentType=="camera_recording_failure"||
        a.IncidentType=="camera_recording_stalled"||a.IncidentType=="camera_storage_limit"||a.IncidentType=="camera_recording_missed"||a.IncidentType=="camera_agent_offline"))||
        (a.SourceType=="Kiosk"&&a.IncidentType=="kiosk_disconnect"));
    public static string Fresh(DateTime? seen,DateTime now,int seconds)=>seen==null?"Unknown":seen<now.AddSeconds(-seconds)?"Offline":"Online";
    public static string KioskConnection(FarmKiosk k,DateTime now,int timeoutSeconds=120)=>k.Status=="Revoked"?"Disabled":k.Status=="Created"?"Unknown":Fresh(k.LastSeenAt,now,timeoutSeconds);
    public static string DeviceConnection(Device d,DateTime now,int timeoutSeconds=900)
    {
        if(d.Status==DeviceStatus.Maintenance)return "Maintenance";
        if(d.EdgeState is "Rejected" or "Revoked")return "Disabled";
        if(d.EdgeState is "Unprovisioned" or "Pending")return "Unknown";
        var fresh=Fresh(d.LastSeenAt,now,timeoutSeconds);
        return fresh!="Online"?fresh:d.Status==DeviceStatus.Online?"Online":d.Status.ToString();
    }
    public async Task<List<MonitoringRow>> ReadAsync(DateTime now,CancellationToken ct,bool includeMeasurements=true)
    {
        var policies=await db.DeviceMonitoringPolicies.AsNoTracking()
            .ToDictionaryAsync(x=>new ValueTuple<string,Guid>(x.SourceType,x.SourceId),ct);
        int Timeout(string type,Guid id,int fallback) =>
            policies.TryGetValue((type,id),out var own)?own.TimeoutSeconds:
            policies.TryGetValue((type,Guid.Empty),out var general)?general.TimeoutSeconds:fallback;
        var devices=await db.Devices.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var sensors=await db.Sensors.AsNoTracking().ToListAsync(ct);
        var kiosks=await db.FarmKiosks.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var areas=await db.FarmingAreas.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        var rows=await db.FarmingRows.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var systems=await db.WaterSystems.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.FarmingAreaId,ct);
        var ports=await db.ControllerPorts.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var portCounts=ports.Values.Where(p=>p.IsPresent).GroupBy(p=>p.DeviceId).ToDictionary(g=>g.Key,g=>g.Count());
        var agents=await db.CameraRecordingAgents.AsNoTracking().ToDictionaryAsync(x=>x.DeviceId,ct);
        var schedules=await (from a in db.CameraScheduleAssignments.AsNoTracking() join s in db.CameraSchedules.AsNoTracking() on a.ScheduleId equals s.Id where !s.Deleted select new {a.DeviceId,s.Name,s.Enabled}).ToDictionaryAsync(x=>x.DeviceId,ct);
        var executions=await db.CameraRecordingExecutions.AsNoTracking().GroupBy(x=>x.DeviceId).Select(g=>g.OrderByDescending(x=>x.StartedAt).ThenByDescending(x=>x.Id).First()).ToDictionaryAsync(x=>x.DeviceId,ct);
        var commands=await db.EdgeCommands.AsNoTracking().Where(x=>x.Status=="pending").GroupBy(x=>x.DeviceCode).Select(g=>new{Code=g.Key,Count=g.Count()}).ToListAsync(ct);
        var alerts=await TechnicalAlerts(db).Where(a=>a.Status==AlertStatus.Active||a.Status==AlertStatus.Acknowledged||a.Status==AlertStatus.InProgress).ToListAsync(ct);
        var alertGroups=alerts.Where(a=>Guid.TryParse(a.SourceId,out _)).GroupBy(a=>(a.SourceType!,Guid.Parse(a.SourceId!))).ToDictionary(g=>g.Key,g=>g.ToList());
        var values=new Dictionary<Guid,WaterMeasurement>();
        var received=new Dictionary<Guid,DateTime?>();
        if(includeMeasurements){
            values=await db.WaterMeasurements.AsNoTracking().GroupBy(x=>x.SensorId).Select(g=>g.OrderByDescending(x=>x.MeasuredAt).ThenByDescending(x=>x.Id).First()).ToDictionaryAsync(x=>x.SensorId,ct);
            received=await db.WaterMeasurements.AsNoTracking().GroupBy(x=>x.SensorId).Select(g=>new{Id=g.Key,At=g.Max(x=>x.ReceivedAt)}).ToDictionaryAsync(x=>x.Id,x=>x.At,ct);
        }
        string? Area(Guid? id)=>areas.GetValueOrDefault(id??Guid.Empty);
        string? Row(Guid? id)=>rows.GetValueOrDefault(id??Guid.Empty)?.Name;
        List<Alert> Alerts(string t,Guid id)=>alertGroups.GetValueOrDefault((t,id))??new();
        string Health(List<Alert> list,string connection)=>list.Any(a=>a.Severity==AlertSeverity.Critical)?"Critical":list.Count>0?"Warning":connection=="Online"?"Healthy":connection is "Offline" or "Error"?"Warning":"Unknown";
        var result=new List<MonitoringRow>();
        foreach(var k in kiosks.Values){
            var a=Alerts("Kiosk",k.Id);var conn=KioskConnection(k,now,Timeout("kiosk",k.Id,120));
            result.Add(new(k.Id,"kiosk",k.Code,k.Name,k.FarmingAreaId,null,Area(k.FarmingAreaId),null,conn,Health(a,conn),k.Status,k.LastSeenAt,Timeout("kiosk",k.Id,120),k.LanIp,ActiveAlertCount:a.Count,ReasonCode:conn=="Offline"?"heartbeat_timeout":null));
        }
        foreach(var d in devices.Values){
            var camera=CameraConfiguration.IsCamera(d);var a=Alerts("Device",d.Id);var type=camera?"camera":"controller";var timeout=Timeout(type,d.Id,900);var conn=DeviceConnection(d,now,timeout);
            var agent=agents.GetValueOrDefault(d.Id);var kioskId=camera?agent?.KioskId:d.KioskId;
            var k=kiosks.GetValueOrDefault(kioskId??Guid.Empty);var ks=k==null?null:KioskConnection(k,now,Timeout("kiosk",k.Id,120));
            var agentStatus=!camera?null:agent==null?"Unassigned":!agent.Enabled||k==null||k.Status=="Revoked"||k.FarmingAreaId!=d.FarmingAreaId?"Disabled":Fresh(agent.LastSeenAt,now,Timeout("agent",d.Id,90));
            var execution=executions.GetValueOrDefault(d.Id);var schedule=schedules.GetValueOrDefault(d.Id);
            var health=Health(a,conn);if(camera&&agentStatus=="Offline"&&health=="Unknown")health="Warning";
            result.Add(new(d.Id,camera?"camera":"controller",d.DeviceCode,d.Name,d.FarmingAreaId,d.FarmingRowId,Area(d.FarmingAreaId),Row(d.FarmingRowId),
                conn,health,camera?null:d.EdgeState,d.LastSeenAt,timeout,d.IpAddress,d.FirmwareVersion?.Contains("://")==true?null:d.FirmwareVersion,
                kioskId,k?.Code,ks,AgentStatus:agentStatus,AgentLastSeenAt:agent?.LastSeenAt,PendingClips:agent?.PendingClips,
                RecordingStatus:schedule==null?"NotScheduled":!schedule.Enabled?"Disabled":execution==null?"Waiting":execution.Status,
                ScheduleName:schedule?.Name,PortCount:portCounts.GetValueOrDefault(d.Id),PendingCommands:commands.Where(c=>string.Equals(c.Code,d.DeviceCode,StringComparison.OrdinalIgnoreCase)).Sum(c=>c.Count),
                ActiveAlertCount:a.Count,ReasonCode:conn=="Offline"?(ks=="Offline"?"upstream_kiosk_offline":"heartbeat_timeout"):conn=="Unknown"?"no_verified_heartbeat":null));
        }
        foreach(var s in sensors){
            var d=devices.GetValueOrDefault(s.DeviceId??Guid.Empty);var p=ports.GetValueOrDefault(s.ControllerPortId??s.BusPortId??Guid.Empty);
            var rowId=s.FarmingRowId??d?.FarmingRowId;var areaId=s.FarmingAreaId??rows.GetValueOrDefault(rowId??Guid.Empty)?.FarmingAreaId??systems.GetValueOrDefault(s.WaterSystemId??Guid.Empty)??d?.FarmingAreaId;
            var a=Alerts("Sensor",s.Id);var timeout=s.DisconnectTimeoutSeconds??900;
            var conn=!s.IsActive?"Disabled":s.DeviceId.HasValue&&(d==null||DeviceConnection(d,now,Timeout("controller",d.Id,900)) is "Offline" or "Maintenance" or "Disabled")?"Offline":
                (s.ControllerPortId.HasValue||s.BusPortId.HasValue)&&(p==null||!p.IsPresent||d==null||p.ConfigVersion!=d.CapabilityVersion)?"Offline":Fresh(s.LastSeenAt,now,timeout);
            var m=values.GetValueOrDefault(s.Id);var k=kiosks.GetValueOrDefault(d?.KioskId??Guid.Empty);
            result.Add(new(s.Id,"sensor",s.SensorCode,s.Name??s.SensorType,areaId,rowId,Area(areaId),Row(rowId),conn,Health(a,conn),null,s.LastSeenAt,timeout,
                KioskId:k?.Id,KioskCode:k?.Code,KioskStatus:k==null?null:KioskConnection(k,now,Timeout("kiosk",k.Id,120)),ControllerId:d?.Id,ControllerCode:d?.DeviceCode,PortCode:p?.PortCode,
                LatestValue:m?.Value,Unit:s.Unit,LatestMeasuredAt:m?.MeasuredAt,LastReceivedAt:received.GetValueOrDefault(s.Id),DataQuality:m?.DataQuality??"Unknown",
                ActiveAlertCount:a.Count,ReasonCode:conn=="Offline"&&d!=null&&DeviceConnection(d,now,Timeout("controller",d.Id,900))=="Offline"?"upstream_controller_offline":conn=="Offline"?"telemetry_timeout":null));
        }
        return result;
    }
}
