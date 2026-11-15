using System.Security.Claims;
using System.Globalization;
using System.Text;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles=AppRoles.Platform)]
[Route("api/system/device-monitoring")]
public sealed class DeviceMonitoringController(AppDbContext db,Microsoft.Extensions.Configuration.IConfiguration? config=null):ControllerBase
{
    private async Task<Guid> Actor(CancellationToken ct){
        if(!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)||
            !await db.AppUsers.AnyAsync(x=>x.Id==id&&x.IsActive&&x.Role==UserRole.SystemAdministrator,ct))throw AppException.Forbidden("System administrator required.");
        return id;
    }
    private static IActionResult Result(object value)=>new OkObjectResult(ApiResponse<object>.Ok(value));
    private static void Paging(int page,int pageSize){if(page<1||pageSize is <1 or >200||(long)(page-1)*pageSize>int.MaxValue)throw AppException.BadRequest("Invalid pagination.");}
    private static IEnumerable<MonitoringRow> Filter(IEnumerable<MonitoringRow> rows,string? type,string? status,Guid? farmingAreaId,Guid? farmingRowId,string? search,bool? hasAlert){
        return rows.Where(x=>(string.IsNullOrWhiteSpace(type)||x.Type==type)&&(string.IsNullOrWhiteSpace(status)||x.ConnectionStatus==status)
            &&(!farmingAreaId.HasValue||x.FarmingAreaId==farmingAreaId)&&(!farmingRowId.HasValue||x.FarmingRowId==farmingRowId)
            &&(!hasAlert.HasValue||(x.ActiveAlertCount>0)==hasAlert.Value)
            &&(string.IsNullOrWhiteSpace(search)||$"{x.Code} {x.Name} {x.IpAddress}".Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)));
    }
    private static object Counts(IEnumerable<MonitoringRow> input){
        var rows=input.ToList();return new{total=rows.Count,online=rows.Count(x=>x.ConnectionStatus=="Online"),offline=rows.Count(x=>x.ConnectionStatus=="Offline"),
            maintenance=rows.Count(x=>x.ConnectionStatus=="Maintenance"),disabled=rows.Count(x=>x.ConnectionStatus=="Disabled"),unknown=rows.Count(x=>x.ConnectionStatus=="Unknown"),
            error=rows.Count(x=>x.ConnectionStatus=="Error"),other=rows.Count(x=>x.ConnectionStatus is not ("Online" or "Offline" or "Maintenance" or "Disabled" or "Unknown" or "Error")),
            warning=rows.Count(x=>x.HealthStatus is "Warning" or "Critical")};
    }
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(Guid? farmingAreaId,CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var rows=Filter(await new DeviceMonitoringReader(db).ReadAsync(now,ct,false),null,null,farmingAreaId,null,null,null).ToList();
        return Result(new{counts=Counts(rows),byType=rows.GroupBy(x=>x.Type).Select(g=>new{type=g.Key,counts=Counts(g)}),checkedAtUtc=now,activeAlerts=rows.Sum(x=>x.ActiveAlertCount)});
    }
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;
        var worker=await db.MonitoringWorkerStates.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkerName=="device-history",ct);
        var last=worker?.LastSucceededAt??await db.DeviceMonitoringStates.AsNoTracking().MaxAsync(x=>(DateTime?)x.ObservedAt,ct);
        var enabled=config?.GetValue("DeviceMonitoring:EnableHistory",true)??true;
        return Result(new{historyEnabled=enabled,lastObservedAt=last,historyStatus=!enabled?"Disabled":last==null?"Unknown":last<now.AddMinutes(-3)?"Stale":"Healthy",checkedAtUtc=now,observationGapSeconds=180,deviceCount=worker?.DeviceCount});
    }
    [HttpGet("devices")]
    public async Task<IActionResult> Devices(string? type,string? status,Guid? farmingAreaId,Guid? farmingRowId,string? search,bool? hasAlert,int page=1,int pageSize=20,CancellationToken ct=default){
        await Actor(ct);Paging(page,pageSize);var now=DateTime.UtcNow;
        var reader=new DeviceMonitoringReader(db);
        var rows=Filter(await reader.ReadAsync(now,ct,false),type,status,farmingAreaId,farmingRowId,search,hasAlert)
            .OrderBy(x=>x.Type).ThenBy(x=>x.Code).ThenBy(x=>x.Id).ToList();
        var items=await reader.AddMeasurementsAsync(rows.Skip((page-1)*pageSize).Take(pageSize).ToList(),ct);
        return Result(new{items,total=rows.Count,page,pageSize,checkedAtUtc=now});
    }
    [HttpGet("{type}/{id:guid}")]
    public async Task<IActionResult> Detail(string type,Guid id,CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var reader=new DeviceMonitoringReader(db);
        var row=(await reader.ReadAsync(now,ct,false)).SingleOrDefault(x=>x.Type==type&&x.Id==id)??throw AppException.NotFound("Device");
        return Result(new{device=(await reader.AddMeasurementsAsync([row],ct)).Single(),checkedAtUtc=now});
    }
    [HttpGet("areas")]
    public async Task<IActionResult> Areas(CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var rows=await new DeviceMonitoringReader(db).ReadAsync(now,ct,false);
        return Result(rows.GroupBy(x=>new{x.FarmingAreaId,x.AreaName}).Select(g=>new{g.Key.FarmingAreaId,g.Key.AreaName,counts=Counts(g)}));
    }
    private static readonly Dictionary<string,int> Defaults = new()
    {
        ["camera"]=900,["controller"]=900,["kiosk"]=120,["agent"]=90
    };
    public sealed record MonitoringPolicyRequest(int TimeoutSeconds,int ExpectedVersion);
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(CancellationToken ct){
        await Actor(ct);
        var policies=await db.DeviceMonitoringPolicies.AsNoTracking().OrderBy(x=>x.SourceType).ThenBy(x=>x.SourceId).ToListAsync(ct);
        return Result(new {
            cameraTimeoutSeconds=policies.FirstOrDefault(x=>x.SourceType=="camera"&&x.SourceId==Guid.Empty)?.TimeoutSeconds??900,
            controllerTimeoutSeconds=policies.FirstOrDefault(x=>x.SourceType=="controller"&&x.SourceId==Guid.Empty)?.TimeoutSeconds??900,
            kioskTimeoutSeconds=policies.FirstOrDefault(x=>x.SourceType=="kiosk"&&x.SourceId==Guid.Empty)?.TimeoutSeconds??120,
            agentTimeoutSeconds=policies.FirstOrDefault(x=>x.SourceType=="agent"&&x.SourceId==Guid.Empty)?.TimeoutSeconds??90,
            sensorDefaultTimeoutSeconds=900,sensorOverrideSupported=true,
            historySampleSeconds=60,historyGapSeconds=180,
            overrides=policies.Select(x=>new{x.SourceType,x.SourceId,timeoutSeconds=x.IsInherited?(int?)null:x.TimeoutSeconds,x.Version,x.UpdatedAt,x.IsInherited}),
            note="Display/history thresholds only. Technical disconnect alert timing retains its existing service policy; Sensor uses its own disconnect timeout."
        });
    }
    [HttpPut("policies/{type}/{id:guid}")]
    public async Task<IActionResult> UpdatePolicy(string type,Guid id,[FromBody] MonitoringPolicyRequest body,CancellationToken ct){
        var actor=await Actor(ct);
        type=type.Trim().ToLowerInvariant();
        if(!Defaults.ContainsKey(type))throw AppException.BadRequest("Unsupported monitoring policy type.");
        if(body.TimeoutSeconds is <30 or >86400 || body.ExpectedVersion<0)throw AppException.BadRequest("Timeout must be 30..86400 seconds and expectedVersion non-negative.");
        if(id!=Guid.Empty){
            if(type=="kiosk"){
                if(!await db.FarmKiosks.AnyAsync(x=>x.Id==id,ct))throw AppException.NotFound("Kiosk");
            }else{
                var d=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Device");
                var isCamera=CameraConfiguration.IsCamera(d);
                if((type=="controller"&&isCamera)||(type is "camera" or "agent"&&!isCamera))throw AppException.BadRequest("Monitoring type does not match device.");
            }
        }
        var policy=await db.DeviceMonitoringPolicies.SingleOrDefaultAsync(x=>x.SourceType==type&&x.SourceId==id,ct);
        if(policy==null){
            if(body.ExpectedVersion!=0)throw AppException.Conflict("Policy was updated. Refresh before saving.");
            policy=new(){SourceType=type,SourceId=id,TimeoutSeconds=body.TimeoutSeconds,Version=1,UpdatedBy=actor};
            db.DeviceMonitoringPolicies.Add(policy);
        }else{
            if(policy.Version!=body.ExpectedVersion)throw AppException.Conflict("Policy was updated. Refresh before saving.");
            policy.TimeoutSeconds=body.TimeoutSeconds;policy.IsInherited=false;policy.Version++;policy.UpdatedBy=actor;
        }
        db.OperationLogs.Add(new(){UserId=actor,Action="UpdateDeviceMonitoringPolicy",EntityType="DeviceMonitoringPolicy",EntityId=policy.Id,Details=$"{type}:{id:N}:{body.TimeoutSeconds}"});
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Concurrent policy update. Refresh and try again.");}
        return Result(new{policy.SourceType,policy.SourceId,policy.TimeoutSeconds,policy.Version});
    }
    [HttpDelete("policies/{type}/{id:guid}")]
    public async Task<IActionResult> ResetPolicy(string type,Guid id,int expectedVersion,CancellationToken ct){
        var actor=await Actor(ct);type=type.Trim().ToLowerInvariant();
        if(id==Guid.Empty||!Defaults.ContainsKey(type)||expectedVersion<0)throw AppException.BadRequest("Only device overrides may be reset.");
        var policy=await db.DeviceMonitoringPolicies.SingleOrDefaultAsync(x=>x.SourceType==type&&x.SourceId==id,ct)??throw AppException.NotFound("Monitoring override");
        if(policy.Version!=expectedVersion)throw AppException.Conflict("Policy changed; reload.");
        if(!policy.IsInherited){
            policy.IsInherited=true;policy.Version++;policy.UpdatedBy=actor;
            db.OperationLogs.Add(new(){UserId=actor,Action="ResetDeviceMonitoringPolicy",EntityType="DeviceMonitoringPolicy",EntityId=policy.Id});
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw AppException.Conflict("Policy changed; reload.");}
        }
        return Result(new{policy.SourceType,policy.SourceId,policy.Version,policy.IsInherited});
    }
    public sealed record MaintenanceRequest(DateTimeOffset StartsAt,DateTimeOffset EndsAt,string? Reason);
    [HttpGet("{type}/{id:guid}/maintenance")]
    public async Task<IActionResult> Maintenance(string type,Guid id,CancellationToken ct){
        await Actor(ct);if(type is not ("camera" or "sensor" or "controller" or "kiosk"))throw AppException.BadRequest("Invalid source type.");
        var now=DateTime.UtcNow;var q=db.DeviceMaintenanceWindows.AsNoTracking().Where(x=>x.SourceType==type&&x.SourceId==id);
        var items=await q.OrderByDescending(x=>x.StartsAt).Take(50).ToListAsync(ct);
        var actorIds=items.Select(x=>x.CreatedBy).Concat(items.Where(x=>x.CancelledBy.HasValue).Select(x=>x.CancelledBy!.Value)).Distinct().ToArray();
        var actors=await db.AppUsers.AsNoTracking().Where(x=>actorIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.FullName,ct);
        return Result(new{items=items.Select(x=>new{x.Id,x.StartsAt,x.EndsAt,x.Reason,createdBy=actors.GetValueOrDefault(x.CreatedBy),x.CancelledAt,
            cancelledBy=x.CancelledBy.HasValue?actors.GetValueOrDefault(x.CancelledBy.Value):null,
            status=x.CancelledAt.HasValue?"Cancelled":x.StartsAt>now?"Scheduled":x.EndsAt<=now?"Completed":"Active"}),
            note="Maintenance plans document work; actual device maintenance uses existing management controls. Plans never mute safety alerts or change relay state."});
    }
    [HttpPost("{type}/{id:guid}/maintenance")]
    public async Task<IActionResult> CreateMaintenance(string type,Guid id,MaintenanceRequest request,CancellationToken ct){
        var actor=await Actor(ct);var now=DateTime.UtcNow;var start=request.StartsAt.UtcDateTime;var end=request.EndsAt.UtcDateTime;var reason=request.Reason?.Trim();
        if(type is not ("camera" or "sensor" or "controller" or "kiosk")||string.IsNullOrWhiteSpace(reason)||reason.Length>500||start<now.AddMinutes(-5)||start>now.AddDays(90)||end<=start||end<=now||end-start>TimeSpan.FromDays(7))
            throw AppException.BadRequest("Use a 1..500 character reason and a maintenance window of up to 7 days within the next 90 days.");
        if(!(await new DeviceMonitoringReader(db).ReadAsync(now,ct,false)).Any(x=>x.Type==type&&x.Id==id))throw AppException.NotFound("Device");
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(tx!=null)await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74638112016)",ct);
        if(await db.DeviceMaintenanceWindows.AnyAsync(x=>x.SourceType==type&&x.SourceId==id&&x.CancelledAt==null&&x.StartsAt<end&&x.EndsAt>start,ct))
            throw AppException.Conflict("Maintenance windows overlap.");
        var row=new DeviceMaintenanceWindow{SourceType=type,SourceId=id,StartsAt=start,EndsAt=end,Reason=reason,CreatedBy=actor};
        db.DeviceMaintenanceWindows.Add(row);
        db.OperationLogs.Add(new(){UserId=actor,Action="ScheduleDeviceMaintenance",EntityType="DeviceMaintenanceWindow",EntityId=row.Id,Details=type+":"+id.ToString("N")});
        await db.SaveChangesAsync(ct);if(tx!=null)await tx.CommitAsync(ct);return Result(new{row.Id});
    }
    [HttpPost("maintenance/{id:guid}/cancel")]
    public async Task<IActionResult> CancelMaintenance(Guid id,CancellationToken ct){
        var actor=await Actor(ct);var now=DateTime.UtcNow;
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(tx!=null)await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74638112016)",ct);
        var row=await db.DeviceMaintenanceWindows.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Maintenance window");
        if(row.CancelledAt.HasValue||row.EndsAt<=now)throw AppException.Conflict("Window already finished or cancelled.");
        row.CancelledAt=now;row.CancelledBy=actor;
        db.OperationLogs.Add(new(){UserId=actor,Action="CancelDeviceMaintenance",EntityType="DeviceMaintenanceWindow",EntityId=id});
        await db.SaveChangesAsync(ct);if(tx!=null)await tx.CommitAsync(ct);return Result(new{id});
    }
    [HttpGet("topology")]
    public async Task<IActionResult> Topology(Guid? farmingAreaId,CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;
        var rows=Filter(await new DeviceMonitoringReader(db).ReadAsync(now,ct,false),null,null,farmingAreaId,null,null,null).ToList();
        var nodes=rows.Select(x=>new{x.Id,x.Type,x.Code,x.Name,x.FarmingAreaId,x.AreaName,x.FarmingRowId,x.RowName,x.KioskId,x.ControllerId,x.PortCode,x.ConnectionStatus,x.HealthStatus,x.ReasonCode});
        var controllerIds=rows.Where(x=>x.Type=="controller").Select(x=>x.Id).ToArray();
        var ports=await db.ControllerPorts.AsNoTracking().Where(x=>controllerIds.Contains(x.DeviceId)).OrderBy(x=>x.PortCode)
            .Select(x=>new{x.Id,x.DeviceId,x.PortCode,x.PortType,x.IsPresent,x.ConfigVersion,x.ReportedOn,x.CommissioningStatus,x.LastReportedAt}).ToListAsync(ct);
        var portIds=ports.Select(x=>x.Id).ToArray();
        var equipment=await db.RasComponents.AsNoTracking().Where(x=>x.ControllerPortId.HasValue&&portIds.Contains(x.ControllerPortId.Value))
            .Select(x=>new{x.Id,x.Name,x.Code,x.Type,x.ControllerPortId,x.LastReportedAt}).ToListAsync(ct);
        return Result(new{nodes,ports,equipment,checkedAtUtc=now,note="Reported output is not proof of physical load operation."});
    }
    [HttpGet("{type}/{id:guid}/diagnostics")]
    public async Task<IActionResult> Diagnostics(string type,Guid id,CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var reader=new DeviceMonitoringReader(db);
        var row=(await reader.ReadAsync(now,ct,false)).SingleOrDefault(x=>x.Type==type&&x.Id==id)??throw AppException.NotFound("Device");
        row=(await reader.AddMeasurementsAsync([row],ct)).Single();
        object? extra=null;
        if(type=="controller"){
            var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
            var ports=await db.ControllerPorts.AsNoTracking().Where(x=>x.DeviceId==id).OrderBy(x=>x.PortCode)
                .Select(x=>new{x.Id,x.PortCode,x.PortType,x.Channel,x.Gpio,x.IsPresent,x.ConfigVersion,x.ReportedOn,x.CommissioningStatus,x.LastReportedAt}).ToListAsync(ct);
            var commands=await db.EdgeCommands.AsNoTracking().Where(x=>x.DeviceCode==d.DeviceCode).OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(50)
                .Select(x=>new{x.Id,x.Channel,x.Command,x.Status,x.CreatedAt,x.DeliveredAt,x.AcknowledgedAt,x.FailedAt,x.ExpiresAt,x.AttemptCount,x.InventoryRevision}).ToListAsync(ct);
            extra=new{d.CapabilityVersion,d.HardwareModel,d.RssiDbm,d.BatteryLevel,d.ControllerCommandStorageReady,d.ControllerSensorReadSupported,d.ControllerRssiDbm,d.ControllerFreeHeapBytes,d.ControllerUptimeSeconds,d.ControllerHealthAt,controllerHealthFresh=d.ControllerHealthAt>=now.AddMinutes(-3),hardwareControlDisabled=CrabSenseBE.Application.Services.HardwareControlGuard.IsDisabled(config),ports,commands};
        }else if(type=="sensor"){
            var s=await db.Sensors.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
            var calibration=await db.SensorCalibrations.AsNoTracking().Where(x=>x.SensorId==id).OrderByDescending(x=>x.PerformedAt).Select(x=>new{x.PerformedAt,x.Result,x.Method}).FirstOrDefaultAsync(ct);
            extra=new{s.SamplingIntervalSeconds,s.ConfigurationVersion,s.AppliedConfigurationVersion,s.LastConfigurationAppliedAt,calibration,
                measurementFresh=row.LatestMeasuredAt.HasValue&&row.LatestMeasuredAt>=now.AddSeconds(-row.TimeoutSeconds)&&row.LatestMeasuredAt<=now.AddSeconds(5)};
        }else if(type=="camera"){
            var latest=await db.CameraRecordingExecutions.AsNoTracking().Where(x=>x.DeviceId==id).OrderByDescending(x=>x.StartedAt).ThenByDescending(x=>x.Id).Select(x=>new{x.Id,x.Status,x.StartedAt,x.EndedAt,x.LastHeartbeatAt,x.MediaAssetId}).FirstOrDefaultAsync(ct);
            var a=await db.CameraRecordingAgents.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==id,ct);
            extra=new{latest,agentVersion=a?.WorkerVersion,streamStatus=row.StreamStatus??"Unverified",row.StreamProbedAt,row.LastFrameObservedAt,note="Stream status is the most recent authenticated frame observation, not live playback."};
        }else if(type=="kiosk"){
            var controllers=await db.Devices.AsNoTracking().CountAsync(x=>x.KioskId==id,ct);
            var cameras=await db.CameraRecordingAgents.AsNoTracking().CountAsync(x=>x.KioskId==id&&x.Enabled,ct);
            var pending=await db.CameraRecordingAgents.AsNoTracking().Where(x=>x.KioskId==id&&x.Enabled).SumAsync(x=>(int?)x.PendingClips,ct);
            var k=await db.FarmKiosks.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
            extra=new{controllers,cameras,pendingClips=pending??0,k.MetricsReportedAt,k.ProcessCpuPercent,k.ProcessMemoryBytes,k.DiskTotalBytes,k.DiskFreeBytes,k.SoftwareVersion,k.EdgeSyncedAt,k.RecordingCheckedAt,k.UploadCheckedAt,metricsFresh=k.MetricsReportedAt>=now.AddMinutes(-3)};
        }
        return Result(new{device=row,extra,checkedAtUtc=now});
    }
    [HttpGet("{type}/{id:guid}/health-history")]
    public async Task<IActionResult> HealthHistory(string type,Guid id,int page=1,int pageSize=50,CancellationToken ct=default){
        await Actor(ct);Paging(page,pageSize);
        if(type is not ("camera" or "sensor" or "controller" or "kiosk"))throw AppException.BadRequest("Invalid source type.");
        var q=db.DeviceHealthEvents.AsNoTracking().Where(x=>x.SourceType==type&&x.SourceId==id);
        var total=await q.CountAsync(ct);
        var items=await q.OrderByDescending(x=>x.OccurredAt).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        return Result(new{items,total,page,pageSize,coverage="sampled_health_changes"});
    }
    [HttpGet("export")]
    public async Task<IActionResult> Export(Guid? farmingAreaId,int days=7,CancellationToken ct=default){
        await Actor(ct);if(days is <1 or >90)throw AppException.BadRequest("Days must be 1..90.");
        var from=DateTime.UtcNow.AddDays(-days);
        var q=db.DeviceConnectionEvents.AsNoTracking().Where(x=>x.OccurredAt>=from&&(!farmingAreaId.HasValue||x.FarmingAreaId==farmingAreaId));
        var events=await q.OrderBy(x=>x.OccurredAt).ThenBy(x=>x.Id).Take(50001).ToListAsync(ct);
        if(events.Count>50000)throw AppException.BadRequest("Too many events; narrow the range.");
        static string Csv(string? value){
            var s=value??"";if(s.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@')s="'"+s;
            return "\""+s.Replace("\"","\"\"")+"\"";
        }
        var b=new StringBuilder("type,id,areaId,occurredAtUtc,previousStatus,newStatus,reasonCode\r\n");
        foreach(var e in events)b.AppendLine(string.Join(",",Csv(e.SourceType),Csv(e.SourceId.ToString()),Csv(e.FarmingAreaId?.ToString()),Csv(e.OccurredAt.ToString("O",CultureInfo.InvariantCulture)),Csv(e.PreviousStatus),Csv(e.NewStatus),Csv(e.ReasonCode)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray(),"text/csv; charset=utf-8","device-connection-history.csv");
    }
    [HttpGet("{type}/{id:guid}/history")]
    public async Task<IActionResult> History(string type,Guid id,int page=1,int pageSize=50,CancellationToken ct=default){
        await Actor(ct);Paging(page,pageSize);var q=db.DeviceConnectionEvents.AsNoTracking().Where(x=>x.SourceType==type&&x.SourceId==id);
        var total=await q.CountAsync(ct);var state=await db.DeviceMonitoringStates.AsNoTracking().SingleOrDefaultAsync(x=>x.SourceType==type&&x.SourceId==id,ct);
        var items=await q.OrderByDescending(x=>x.OccurredAt).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        return Result(new{items,total,page,pageSize,lastObservedAt=state?.ObservedAt,coverage="sampled_transitions_only"});
    }
    [HttpGet("{type}/{id:guid}/availability")]
    public async Task<IActionResult> Availability(string type,Guid id,int days=7,CancellationToken ct=default){
        await Actor(ct);
        if(type is not ("camera" or "sensor" or "controller" or "kiosk"))throw AppException.BadRequest("Invalid source type.");
        if(days is <1 or >90)throw AppException.BadRequest("Days must be 1..90.");
        var to=DateTime.UtcNow;
        var from=to.AddDays(-days);
        var reader=new DeviceMonitoringReader(db);
        if(!(await reader.ReadAsync(to,ct,false)).Any(x=>x.Type==type&&x.Id==id))throw AppException.NotFound("Device");
        var q=db.DeviceConnectionEvents.AsNoTracking().Where(x=>x.SourceType==type&&x.SourceId==id);
        var prior=await q.Where(x=>x.OccurredAt<from).OrderByDescending(x=>x.OccurredAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        var recent=await q.Where(x=>x.OccurredAt>=from&&x.OccurredAt<=to).OrderBy(x=>x.OccurredAt).ThenBy(x=>x.Id).Take(50001).ToListAsync(ct);
        if(recent.Count>50000)throw AppException.BadRequest("Too many observations; select a shorter range.");
        if(prior!=null)recent.Insert(0,prior);
        var state=await db.DeviceMonitoringStates.AsNoTracking().SingleOrDefaultAsync(x=>x.SourceType==type&&x.SourceId==id,ct);
        return Result(DeviceAvailability.Calculate(recent,state,from,to));
    }
    [HttpGet("trends")]
    public async Task<IActionResult> Trends(Guid? farmingAreaId,int days=7,CancellationToken ct=default){
        await Actor(ct);if(days is <1 or >90)throw AppException.BadRequest("Days must be between 1 and 90.");
        var start=DateTime.UtcNow.Date.AddDays(1-days);
        var q=db.DeviceConnectionEvents.AsNoTracking().Where(x=>x.OccurredAt>=start&&(!farmingAreaId.HasValue||x.FarmingAreaId==farmingAreaId));
        var points=await q.GroupBy(x=>x.OccurredAt.Date).Select(g=>new{day=g.Key,offline=g.Count(x=>x.PreviousStatus!=null&&x.NewStatus=="Offline"),
            recovered=g.Count(x=>x.PreviousStatus=="Offline"&&x.NewStatus=="Online"),gaps=g.Count(x=>x.ReasonCode=="observation_gap"),observations=g.Count()}).OrderBy(x=>x.day).ToListAsync(ct);
        return Result(new{points,fromUtc=start,coverage="sampled_transitions_only"});
    }
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(string? sourceType,Guid? sourceId,Guid? farmingAreaId,string? severity,bool activeOnly=true,int page=1,int pageSize=20,CancellationToken ct=default,string? alertStatus=null){
        await Actor(ct);Paging(page,pageSize);var q=DeviceMonitoringReader.TechnicalAlerts(db);
        if(activeOnly)q=q.Where(a=>a.Status==AlertStatus.Active||a.Status==AlertStatus.Acknowledged||a.Status==AlertStatus.InProgress);
        if(!string.IsNullOrWhiteSpace(alertStatus)){if(!Enum.TryParse<AlertStatus>(alertStatus,true,out var parsed)||!Enum.IsDefined(parsed))throw AppException.BadRequest("Invalid alert status.");q=q.Where(a=>a.Status==parsed);}
        if(!string.IsNullOrWhiteSpace(sourceType))q=q.Where(a=>a.SourceType==sourceType);
        if(sourceId.HasValue){var n=sourceId.Value.ToString("N");var d=sourceId.Value.ToString();q=q.Where(a=>a.SourceId==n||a.SourceId==d);}
        if(farmingAreaId.HasValue)q=q.Where(a=>a.FarmingAreaId==farmingAreaId);
        if(!string.IsNullOrWhiteSpace(severity)){if(!Enum.TryParse<AlertSeverity>(severity,true,out var s))throw AppException.BadRequest("Invalid severity.");q=q.Where(a=>a.Severity==s);}
        var total=await q.CountAsync(ct);
        var pageAlerts=await q.OrderByDescending(a=>a.LastOccurredAt??a.CreatedAt).ThenBy(a=>a.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        var monitored=await new DeviceMonitoringReader(db).ReadAsync(DateTime.UtcNow,ct,false);
        var devices=monitored.ToDictionary(x=>(x.Type,x.Id));
        var items=pageAlerts.Select(a=>{
            var sourceId=Guid.TryParse(a.SourceId,out var fromSource)?fromSource:a.SensorId??Guid.Empty;
            var sourceType=a.SourceType=="Device"?(devices.ContainsKey(("camera",sourceId))?"camera":"controller"):a.SourceType?.ToLowerInvariant();
            var target=sourceType!=null?devices.GetValueOrDefault((sourceType,sourceId)):null;
            return new{a.Id,a.SourceType,a.SourceId,a.FarmingAreaId,a.IncidentType,a.Message,
                severity=a.Severity.ToString(),status=a.Status.ToString(),a.CreatedAt,a.LastOccurredAt,a.OccurrenceCount,a.AcknowledgedAt,a.AcknowledgedBy,a.ProcessingStartedAt,a.ProcessingBy,a.ResolvedAt,a.ResolvedBy,a.ResolutionReason,
                targetType=target?.Type,targetId=target?.Id,sourceCode=target?.Code};
        }).ToList();
        return Result(new{items,total,page,pageSize});
    }
    public sealed record IncidentNote(string? Note);
    [HttpGet("alerts/{id:guid}")]
    public async Task<IActionResult> AlertDetail(Guid id,CancellationToken ct){
        await Actor(ct);
        var a=await DeviceMonitoringReader.TechnicalAlerts(db).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Technical alert");
        var ids=new[]{a.AcknowledgedBy,a.ProcessingBy,a.ResolvedBy}.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();
        var users=await db.AppUsers.AsNoTracking().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.FullName,ct);
        string? Name(Guid? actor)=>actor.HasValue?users.GetValueOrDefault(actor.Value):null;
        var actions=await db.OperationLogs.AsNoTracking().Where(x=>x.EntityType=="Alert"&&x.EntityId==id&&
            (x.Action=="AcknowledgeDeviceMonitoringAlert"||x.Action=="ProcessDeviceMonitoringAlert"||x.Action=="NoteDeviceMonitoringAlert"||x.Action=="CloseDeviceMonitoringAlert"))
            .OrderByDescending(x=>x.CreatedAt).Take(100).Select(x=>new{x.Id,x.Action,x.Details,x.CreatedAt,x.UserId,actor=x.User==null?null:x.User.FullName}).ToListAsync(ct);
        return Result(new{a.Id,a.Message,a.IncidentType,severity=a.Severity.ToString(),status=a.Status.ToString(),a.CreatedAt,a.LastOccurredAt,a.OccurrenceCount,
            a.AcknowledgedAt,acknowledgedBy=Name(a.AcknowledgedBy),a.ProcessingStartedAt,processingBy=Name(a.ProcessingBy),a.ResolvedAt,resolvedBy=Name(a.ResolvedBy),a.ResolutionReason,a.ResolutionNote,actions});
    }
    [HttpPost("alerts/{id:guid}/notes")]
    public async Task<IActionResult> NoteAlert(Guid id,IncidentNote request,CancellationToken ct){
        var actor=await Actor(ct);var note=request.Note?.Trim();
        if(string.IsNullOrWhiteSpace(note)||note.Length>1000)throw AppException.BadRequest("Note must contain 1..1000 characters.");
        if(!await DeviceMonitoringReader.TechnicalAlerts(db).AnyAsync(x=>x.Id==id,ct))throw AppException.NotFound("Technical alert");
        db.OperationLogs.Add(new(){UserId=actor,Action="NoteDeviceMonitoringAlert",EntityType="Alert",EntityId=id,Details=note});
        await db.SaveChangesAsync(ct);return Result(new{id});
    }
    [HttpPost("alerts/{id:guid}/process")]
    public async Task<IActionResult> ProcessAlert(Guid id,IncidentNote request,CancellationToken ct){
        var actor=await Actor(ct);var note=request.Note?.Trim();if(note?.Length>1000)throw AppException.BadRequest("Note is too long.");
        var q=DeviceMonitoringReader.TechnicalAlerts(db).Where(x=>x.Id==id);var now=DateTime.UtcNow;
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(!await q.AnyAsync(ct))throw AppException.NotFound("Technical alert");
        if(tx!=null){
            var changed=await q.Where(x=>x.Status==AlertStatus.Acknowledged).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.Status,AlertStatus.InProgress).SetProperty(x=>x.ProcessingBy,(Guid?)actor).SetProperty(x=>x.ProcessingStartedAt,(DateTime?)now).SetProperty(x=>x.UpdatedAt,(DateTime?)now),ct);
            if(changed!=1)throw AppException.Conflict("Alert must be acknowledged before processing.");
        }else{
            var a=await db.Alerts.SingleAsync(x=>x.Id==id,ct);
            if(a.Status!=AlertStatus.Acknowledged)throw AppException.Conflict("Alert must be acknowledged before processing.");
            a.Status=AlertStatus.InProgress;a.ProcessingBy=actor;a.ProcessingStartedAt=now;
        }
        db.OperationLogs.Add(new(){UserId=actor,Action="ProcessDeviceMonitoringAlert",EntityType="Alert",EntityId=id,Details=note});
        await db.SaveChangesAsync(ct);if(tx!=null)await tx.CommitAsync(ct);return Result(new{id,status="InProgress"});
    }
    [HttpPost("alerts/{id:guid}/close")]
    public async Task<IActionResult> CloseAlert(Guid id,IncidentNote request,CancellationToken ct){
        var actor=await Actor(ct);var note=request.Note?.Trim();if(string.IsNullOrWhiteSpace(note)||note.Length>1000)throw AppException.BadRequest("Closure note must contain 1..1000 characters.");
        var q=DeviceMonitoringReader.TechnicalAlerts(db).Where(x=>x.Id==id);var now=DateTime.UtcNow;
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(!await q.AnyAsync(ct))throw AppException.NotFound("Technical alert");
        if(tx!=null){
            var changed=await q.Where(x=>x.Status==AlertStatus.Recovered).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.Status,AlertStatus.Resolved).SetProperty(x=>x.ResolvedBy,(Guid?)actor).SetProperty(x=>x.ResolutionNote,note).SetProperty(x=>x.UpdatedAt,(DateTime?)now),ct);
            if(changed!=1)throw AppException.Conflict("Only an alert with recovery evidence may be closed.");
        }else{
            var a=await db.Alerts.SingleAsync(x=>x.Id==id,ct);
            if(a.Status!=AlertStatus.Recovered)throw AppException.Conflict("Only an alert with recovery evidence may be closed.");
            a.Status=AlertStatus.Resolved;a.ResolvedBy=actor;a.ResolutionNote=note;
        }
        db.OperationLogs.Add(new(){UserId=actor,Action="CloseDeviceMonitoringAlert",EntityType="Alert",EntityId=id,Details=note});
        await db.SaveChangesAsync(ct);if(tx!=null)await tx.CommitAsync(ct);return Result(new{id,status="Resolved"});
    }
    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id,CancellationToken ct){
        var actor=await Actor(ct);
        var technical=DeviceMonitoringReader.TechnicalAlerts(db).Where(a=>a.Id==id);
        var now=DateTime.UtcNow;
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(!await technical.AnyAsync(ct))throw AppException.NotFound("Technical alert");
        if(tx!=null){
            var count=await technical.Where(a=>a.Status==AlertStatus.Active).ExecuteUpdateAsync(setters=>setters
                .SetProperty(a=>a.Status,AlertStatus.Acknowledged)
                .SetProperty(a=>a.AcknowledgedAt,(DateTime?)now)
                .SetProperty(a=>a.AcknowledgedBy,(Guid?)actor)
                .SetProperty(a=>a.UpdatedAt,(DateTime?)now),ct);
            if(count!=1)throw AppException.Conflict("Only active alerts can be acknowledged.");
        }else{
            var alert=await db.Alerts.SingleAsync(a=>a.Id==id,ct);
            if(alert.Status!=AlertStatus.Active)throw AppException.Conflict("Only active alerts can be acknowledged.");
            alert.Status=AlertStatus.Acknowledged;alert.AcknowledgedAt=now;alert.AcknowledgedBy=actor;
        }
        db.OperationLogs.Add(new(){UserId=actor,Action="AcknowledgeDeviceMonitoringAlert",EntityType="Alert",EntityId=id});
        await db.SaveChangesAsync(ct);
        if(tx!=null)await tx.CommitAsync(ct);
        return Result(new{id,status=AlertStatus.Acknowledged.ToString()});
    }
}
