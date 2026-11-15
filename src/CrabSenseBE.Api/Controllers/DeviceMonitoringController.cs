using System.Security.Claims;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles=AppRoles.Platform)]
[Route("api/system/device-monitoring")]
public sealed class DeviceMonitoringController(AppDbContext db):ControllerBase
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
    [HttpGet("devices")]
    public async Task<IActionResult> Devices(string? type,string? status,Guid? farmingAreaId,Guid? farmingRowId,string? search,bool? hasAlert,int page=1,int pageSize=20,CancellationToken ct=default){
        await Actor(ct);Paging(page,pageSize);var now=DateTime.UtcNow;
        var reader=new DeviceMonitoringReader(db);var rows=Filter(await reader.ReadAsync(now,ct,false),type,status,farmingAreaId,farmingRowId,search,hasAlert).OrderBy(x=>x.Type).ThenBy(x=>x.Code).ThenBy(x=>x.Id).ToList();
        var items=await reader.AddMeasurementsAsync(rows.Skip((page-1)*pageSize).Take(pageSize).ToList(),ct);
        return Result(new{items,total=rows.Count,page,pageSize,checkedAtUtc=now});
    }
    [HttpGet("{type}/{id:guid}")]
    public async Task<IActionResult> Detail(string type,Guid id,CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var reader=new DeviceMonitoringReader(db);var row=(await reader.ReadAsync(now,ct,false)).SingleOrDefault(x=>x.Type==type&&x.Id==id)??throw AppException.NotFound("Device");
        return Result(new{device=(await reader.AddMeasurementsAsync([row],ct)).Single(),checkedAtUtc=now});
    }
    [HttpGet("areas")]
    public async Task<IActionResult> Areas(CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var rows=await new DeviceMonitoringReader(db).ReadAsync(now,ct,false);
        return Result(rows.GroupBy(x=>new{x.FarmingAreaId,x.AreaName}).Select(g=>new{g.Key.FarmingAreaId,g.Key.AreaName,counts=Counts(g)}));
    }
    public sealed record PolicyUpdate(int HeartbeatTimeoutSeconds,int ExpectedVersion);
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(CancellationToken ct){
        await Actor(ct);var t=await new DeviceMonitoringPolicyStore(db).GetAsync(ct);
        var rows=await db.DeviceMonitoringPolicies.AsNoTracking().ToDictionaryAsync(x=>x.SourceType,ct);
        var items=new[]{("camera",t.CameraSeconds),("controller",t.ControllerSeconds),("kiosk",t.KioskSeconds)}
            .Select(x=>new{sourceType=x.Item1,heartbeatTimeoutSeconds=x.Item2,version=rows.GetValueOrDefault(x.Item1)?.Version??0});
        return Result(new{items,cameraTimeoutSeconds=t.CameraSeconds,controllerTimeoutSeconds=t.ControllerSeconds,kioskTimeoutSeconds=t.KioskSeconds,
            legacyControllerTimeoutSeconds=t.LegacyControllerSeconds,agentTimeoutSeconds=90,sensorDefaultTimeoutSeconds=900,sensorOverrideSupported=true,
            historySampleSeconds=60,historyGapSeconds=180});
    }
    [HttpPut("policies/{type}")]
    public async Task<IActionResult> SavePolicy(string type,PolicyUpdate request,CancellationToken ct){
        var actor=await Actor(ct);
        if(type is not ("camera" or "controller" or "kiosk")||request.HeartbeatTimeoutSeconds is <60 or >86400)
            throw AppException.BadRequest("Timeout must be 60–86400 seconds for camera/controller/kiosk.");
        var row=await db.DeviceMonitoringPolicies.SingleOrDefaultAsync(x=>x.SourceType==type,ct);
        if(request.ExpectedVersion!=(row?.Version??0))throw AppException.Conflict("Policy changed; reload.");
        if(row==null){row=new(){SourceType=type};db.DeviceMonitoringPolicies.Add(row);}else row.Version++;
        row.HeartbeatTimeoutSeconds=request.HeartbeatTimeoutSeconds;row.UpdatedBy=actor;row.UpdatedAt=DateTime.UtcNow;
        db.OperationLogs.Add(new(){UserId=actor,Action="UpdateDeviceMonitoringPolicy",EntityType="DeviceMonitoringPolicy",EntityId=row.Id,Details=type+":"+request.HeartbeatTimeoutSeconds});
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Policy changed; reload.");}
        return Result(new{row.SourceType,row.HeartbeatTimeoutSeconds,row.Version});
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
        await Actor(ct);if(days is <1 or >90)throw AppException.BadRequest("Days must be between 1 and 90.");
        var to=DateTime.UtcNow;var from=to.AddDays(-days);
        var q=db.DeviceConnectionEvents.AsNoTracking().Where(x=>x.SourceType==type&&x.SourceId==id);
        var prior=await q.Where(x=>x.OccurredAt<from).OrderByDescending(x=>x.OccurredAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        var events=await q.Where(x=>x.OccurredAt>=from&&x.OccurredAt<=to).OrderBy(x=>x.OccurredAt).ThenBy(x=>x.Id).Take(50001).ToListAsync(ct);
        if(events.Count>50000)throw AppException.BadRequest("Too many observations; use a shorter range.");
        if(prior!=null)events.Insert(0,prior);
        var state=await db.DeviceMonitoringStates.AsNoTracking().SingleOrDefaultAsync(x=>x.SourceType==type&&x.SourceId==id,ct);
        return Result(DeviceAvailability.Calculate(events,state,from,to));
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
    public async Task<IActionResult> Alerts(string? sourceType,Guid? sourceId,Guid? farmingAreaId,string? severity,bool activeOnly=true,int page=1,int pageSize=20,CancellationToken ct=default){
        await Actor(ct);Paging(page,pageSize);var q=DeviceMonitoringReader.TechnicalAlerts(db);
        if(activeOnly)q=q.Where(a=>a.Status==AlertStatus.Active||a.Status==AlertStatus.Acknowledged||a.Status==AlertStatus.InProgress);
        if(!string.IsNullOrWhiteSpace(sourceType))q=q.Where(a=>a.SourceType==sourceType);
        if(sourceId.HasValue){var n=sourceId.Value.ToString("N");var d=sourceId.Value.ToString();q=q.Where(a=>a.SourceId==n||a.SourceId==d);}
        if(farmingAreaId.HasValue)q=q.Where(a=>a.FarmingAreaId==farmingAreaId);
        if(!string.IsNullOrWhiteSpace(severity)){if(!Enum.TryParse<AlertSeverity>(severity,true,out var s))throw AppException.BadRequest("Invalid severity.");q=q.Where(a=>a.Severity==s);}
        var total=await q.CountAsync(ct);
        var alerts=await q.OrderByDescending(a=>a.LastOccurredAt??a.CreatedAt).ThenBy(a=>a.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        var rows=await new DeviceMonitoringReader(db).ReadAsync(DateTime.UtcNow,ct,false);
        var items=alerts.Select(a=>{
            var id=Guid.TryParse(a.SourceId,out var sourceId)?sourceId:a.SensorId;
            var target=rows.FirstOrDefault(x=>x.Id==id&&(a.SourceType=="Device"?x.Type=="camera"||x.Type=="controller":x.Type==a.SourceType?.ToLowerInvariant()));
            return new{a.Id,a.SourceType,a.SourceId,a.FarmingAreaId,a.IncidentType,a.Message,severity=a.Severity.ToString(),status=a.Status.ToString(),a.CreatedAt,a.LastOccurredAt,a.OccurrenceCount,a.AcknowledgedAt,a.ResolvedAt,targetId=target?.Id,targetType=target?.Type,sourceCode=target?.Code};
        });
        return Result(new{items,total,page,pageSize});
    }
    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id,CancellationToken ct){
        var actor=await Actor(ct);if(!await DeviceMonitoringReader.TechnicalAlerts(db).AnyAsync(a=>a.Id==id,ct))throw AppException.NotFound("Technical alert");
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        var now=DateTime.UtcNow;
        if(db.Database.IsRelational()){
            var count=await db.Alerts.Where(x=>x.Id==id&&x.Status==AlertStatus.Active).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,AlertStatus.Acknowledged).SetProperty(x=>x.AcknowledgedAt,(DateTime?)now).SetProperty(x=>x.AcknowledgedBy,(Guid?)actor),ct);
            if(count!=1)throw AppException.Conflict("Only active alerts can be acknowledged.");
        }else{
            var a=await db.Alerts.SingleAsync(x=>x.Id==id,ct);
            if(a.Status!=AlertStatus.Active)throw AppException.Conflict("Only active alerts can be acknowledged.");
            a.Status=AlertStatus.Acknowledged;a.AcknowledgedAt=now;a.AcknowledgedBy=actor;
        }
        db.OperationLogs.Add(new(){UserId=actor,Action="AcknowledgeDeviceMonitoringAlert",EntityType="Alert",EntityId=id});
        await db.SaveChangesAsync(ct);if(tx!=null)await tx.CommitAsync(ct);
        return Result(new{id,status=AlertStatus.Acknowledged.ToString()});
    }
}
