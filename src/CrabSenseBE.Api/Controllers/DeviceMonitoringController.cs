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
        var rows=Filter(await new DeviceMonitoringReader(db).ReadAsync(now,ct),type,status,farmingAreaId,farmingRowId,search,hasAlert).OrderBy(x=>x.Type).ThenBy(x=>x.Code).ThenBy(x=>x.Id).ToList();
        return Result(new{items=rows.Skip((page-1)*pageSize).Take(pageSize),total=rows.Count,page,pageSize,checkedAtUtc=now});
    }
    [HttpGet("{type}/{id:guid}")]
    public async Task<IActionResult> Detail(string type,Guid id,CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var row=(await new DeviceMonitoringReader(db).ReadAsync(now,ct)).SingleOrDefault(x=>x.Type==type&&x.Id==id)??throw AppException.NotFound("Device");
        return Result(new{device=row,checkedAtUtc=now});
    }
    [HttpGet("areas")]
    public async Task<IActionResult> Areas(CancellationToken ct){
        await Actor(ct);var now=DateTime.UtcNow;var rows=await new DeviceMonitoringReader(db).ReadAsync(now,ct,false);
        return Result(rows.GroupBy(x=>new{x.FarmingAreaId,x.AreaName}).Select(g=>new{g.Key.FarmingAreaId,g.Key.AreaName,counts=Counts(g)}));
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
        var items=await q.OrderByDescending(a=>a.LastOccurredAt??a.CreatedAt).ThenBy(a=>a.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(a=>new{a.Id,a.SourceType,a.SourceId,a.FarmingAreaId,a.IncidentType,a.Message,severity=a.Severity.ToString(),status=a.Status.ToString(),a.CreatedAt,a.LastOccurredAt,a.OccurrenceCount,a.AcknowledgedAt,a.ResolvedAt}).ToListAsync(ct);
        return Result(new{items,total,page,pageSize});
    }
    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id,CancellationToken ct){
        var actor=await Actor(ct);if(!await DeviceMonitoringReader.TechnicalAlerts(db).AnyAsync(a=>a.Id==id,ct))throw AppException.NotFound("Technical alert");
        var a=await db.Alerts.SingleAsync(x=>x.Id==id,ct);
        if(a.Status!=AlertStatus.Active)throw AppException.Conflict("Only active alerts can be acknowledged.");
        a.Status=AlertStatus.Acknowledged;a.AcknowledgedAt=DateTime.UtcNow;a.AcknowledgedBy=actor;
        db.OperationLogs.Add(new(){UserId=actor,Action="AcknowledgeDeviceMonitoringAlert",EntityType="Alert",EntityId=id});
        await db.SaveChangesAsync(ct);return Result(new{a.Id,status=a.Status.ToString()});
    }
}
