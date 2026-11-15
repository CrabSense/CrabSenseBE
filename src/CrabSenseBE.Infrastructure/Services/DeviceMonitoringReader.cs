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
    string? ScheduleName=null,int PortCount=0,int PendingCommands=0,int ActiveAlertCount=0,string? ReasonCode=null,
    string? StreamVerification=null,DateTime? LastRecordingAt=null);

public sealed class DeviceMonitoringReader(AppDbContext db)
{
    public static bool IsOpen(Alert a)=>a.Status is AlertStatus.Active or AlertStatus.Acknowledged or AlertStatus.InProgress;
    public static IQueryable<Alert> TechnicalAlerts(AppDbContext db)=>db.Alerts.AsNoTracking().Where(a=>
        (a.SourceType=="Sensor"&&(a.IncidentType=="sensor_disconnect"||a.IncidentType=="sensor_read_error"))||
        (a.SourceType=="Device"&&(a.IncidentType=="device_disconnect"||a.IncidentType=="camera_recording_failure"||
        a.IncidentType=="camera_recording_stalled"||a.IncidentType=="camera_storage_limit"||a.IncidentType=="camera_recording_missed"||a.IncidentType=="camera_agent_offline"))||
        (a.SourceType=="Kiosk"&&a.IncidentType=="kiosk_disconnect"));
    public static string Fresh(DateTime? seen,DateTime now,int seconds)=>DeviceConnectionPolicy.Fresh(seen,now,seconds);
    public static string KioskConnection(FarmKiosk k,DateTime now)=>DeviceConnectionPolicy.KioskStatusOf(k,now,new());
    public static string DeviceConnection(Device d,DateTime now)=>DeviceConnectionPolicy.DeviceStatusOf(d,now,new());
    public async Task<List<MonitoringRow>> AddMeasurementsAsync(List<MonitoringRow> rows,CancellationToken ct)
    {
        var ids=rows.Where(x=>x.Type=="sensor").Select(x=>x.Id).ToArray();
        if(ids.Length==0)return rows;
        var values=await db.WaterMeasurements.AsNoTracking().Where(x=>ids.Contains(x.SensorId)).GroupBy(x=>x.SensorId)
            .Select(g=>g.OrderByDescending(x=>x.MeasuredAt).ThenByDescending(x=>x.Id).First()).ToDictionaryAsync(x=>x.SensorId,ct);
        var received=await db.WaterMeasurements.AsNoTracking().Where(x=>ids.Contains(x.SensorId)).GroupBy(x=>x.SensorId)
            .Select(g=>new{Id=g.Key,At=g.Max(x=>x.ReceivedAt)}).ToDictionaryAsync(x=>x.Id,x=>x.At,ct);
        return rows.Select(x=>x.Type!="sensor"?x:x with {LatestValue=values.GetValueOrDefault(x.Id)?.Value,
            LatestMeasuredAt=values.GetValueOrDefault(x.Id)?.MeasuredAt,LastReceivedAt=received.GetValueOrDefault(x.Id),
            DataQuality=values.GetValueOrDefault(x.Id)?.DataQuality??"Unknown"}).ToList();
    }
    public async Task<List<MonitoringRow>> ReadAsync(DateTime now,CancellationToken ct,bool includeMeasurements=true)
    {
        var timeouts=await new DeviceMonitoringPolicyStore(db).GetAsync(ct);
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
        string? Area(Guid? id)=>areas.GetValueOrDefault(id??Guid.Empty);
        string? Row(Guid? id)=>rows.GetValueOrDefault(id??Guid.Empty)?.Name;
        List<Alert> Alerts(string t,Guid id)=>alertGroups.GetValueOrDefault((t,id))??new();
        string Health(List<Alert> list)=>list.Count==0?"Unknown":list.Any(a=>a.Severity==AlertSeverity.Critical)?"Critical":"Warning";
        var result=new List<MonitoringRow>();
        foreach(var k in kiosks.Values){
            var a=Alerts("Kiosk",k.Id);var conn=DeviceConnectionPolicy.KioskStatusOf(k,now,timeouts);
            result.Add(new(k.Id,"kiosk",k.Code,k.Name,k.FarmingAreaId,null,Area(k.FarmingAreaId),null,conn,Health(a),k.Status,k.LastSeenAt,timeouts.KioskSeconds,k.LanIp,ActiveAlertCount:a.Count,ReasonCode:conn=="Offline"?"heartbeat_timeout":null));
        }
        foreach(var d in devices.Values){
            var camera=CameraConfiguration.IsCamera(d);var a=Alerts("Device",d.Id);var conn=DeviceConnectionPolicy.DeviceStatusOf(d,now,timeouts);
            var agent=agents.GetValueOrDefault(d.Id);var kioskId=camera?agent?.KioskId:d.KioskId;
            var k=kiosks.GetValueOrDefault(kioskId??Guid.Empty);var ks=k==null?null:DeviceConnectionPolicy.KioskStatusOf(k,now,timeouts);
            var agentStatus=!camera?null:agent==null?"Unassigned":!agent.Enabled||k==null||k.Status=="Revoked"||k.FarmingAreaId!=d.FarmingAreaId?"Disabled":Fresh(agent.LastSeenAt,now,90);
            var execution=executions.GetValueOrDefault(d.Id);var schedule=schedules.GetValueOrDefault(d.Id);
            var health=Health(a);if(camera&&agentStatus=="Offline"&&health=="Unknown")health="Warning";
            result.Add(new(d.Id,camera?"camera":"controller",d.DeviceCode,d.Name,d.FarmingAreaId,d.FarmingRowId,Area(d.FarmingAreaId),Row(d.FarmingRowId),
                conn,health,camera?null:d.EdgeState,d.LastSeenAt,timeouts.ForDevice(d),d.IpAddress,d.FirmwareVersion?.Contains("://")==true?null:d.FirmwareVersion,
                kioskId,k?.Code,ks,AgentStatus:agentStatus,AgentLastSeenAt:agent?.LastSeenAt,PendingClips:agent?.PendingClips,
                RecordingStatus:!camera?null:schedule==null?"NotScheduled":!schedule.Enabled?"Disabled":execution==null?"Waiting":execution.Status=="Recording"&&execution.LastHeartbeatAt<now.AddSeconds(-90)?"Stalled":execution.Status,
                ScheduleName:schedule?.Name,PortCount:portCounts.GetValueOrDefault(d.Id),PendingCommands:commands.Where(c=>string.Equals(c.Code,d.DeviceCode,StringComparison.OrdinalIgnoreCase)).Sum(c=>c.Count),
                ActiveAlertCount:a.Count,ReasonCode:conn=="Offline"?(ks=="Offline"?"upstream_kiosk_offline":"heartbeat_timeout"):conn=="Unknown"?"no_verified_heartbeat":null,
                StreamVerification:camera?"NotVerified":null,LastRecordingAt:camera?execution?.EndedAt:null));
        }
        foreach(var s in sensors){
            var d=devices.GetValueOrDefault(s.DeviceId??Guid.Empty);var p=ports.GetValueOrDefault(s.ControllerPortId??s.BusPortId??Guid.Empty);
            var rowId=s.FarmingRowId??d?.FarmingRowId;var areaId=s.FarmingAreaId??rows.GetValueOrDefault(rowId??Guid.Empty)?.FarmingAreaId??systems.GetValueOrDefault(s.WaterSystemId??Guid.Empty)??d?.FarmingAreaId;
            var a=Alerts("Sensor",s.Id);var timeout=s.DisconnectTimeoutSeconds??900;
            var conn=DeviceConnectionPolicy.SensorStatusOf(s,d,p,now,timeouts);
            var k=kiosks.GetValueOrDefault(d?.KioskId??Guid.Empty);
            result.Add(new(s.Id,"sensor",s.SensorCode,s.Name??s.SensorType,areaId,rowId,Area(areaId),Row(rowId),conn,Health(a),null,s.LastSeenAt,timeout,
                KioskId:k?.Id,KioskCode:k?.Code,KioskStatus:k==null?null:DeviceConnectionPolicy.KioskStatusOf(k,now,timeouts),ControllerId:d?.Id,ControllerCode:d?.DeviceCode,PortCode:p?.PortCode,
                Unit:s.Unit,DataQuality:"Unknown",
                ActiveAlertCount:a.Count,ReasonCode:conn=="Offline"&&d!=null&&DeviceConnectionPolicy.DeviceStatusOf(d,now,timeouts)=="Offline"?"upstream_controller_offline":conn=="Offline"?"telemetry_timeout":null));
        }
        return includeMeasurements?await AddMeasurementsAsync(result,ct):result;
    }
}
