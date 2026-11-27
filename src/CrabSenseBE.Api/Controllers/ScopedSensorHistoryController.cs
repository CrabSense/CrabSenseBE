using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles=AppRoles.FarmPortal)]
[Route("api/areas/{areaId:guid}/sensors/{sensorId:guid}/history")]
public sealed class ScopedSensorHistoryController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid areaId,Guid sensorId,[FromQuery] DateTimeOffset from,[FromQuery] DateTimeOffset to,
        [FromQuery] Guid? farmingRowId=null,[FromQuery] DateTimeOffset? asOf=null,[FromQuery] int page=1,
        [FromQuery] int pageSize=250,CancellationToken ct=default)
    {
        var now=DateTimeOffset.UtcNow;
        var cutoff=asOf??now;
        if(areaId==Guid.Empty||sensorId==Guid.Empty||farmingRowId==Guid.Empty||from==default||to==default||
            from>=to||to-from>TimeSpan.FromDays(31)||to>now.AddSeconds(1)||cutoff>now.AddSeconds(1)||cutoff<to||
            page is <1 or >10000||pageSize is <1 or >500||(page>1&&asOf is null))
            throw AppException.BadRequest("Invalid scoped history range or pagination.");
        await new FarmAreaScope(uow,user).EnsureAsync(areaId,false,ct);
        Response.Headers["Cache-Control"]="no-store";
        var sensor=await db.Sensors.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==sensorId,ct);
        if(sensor is null)throw AppException.NotFound("Sensor");
        var device=sensor.DeviceId is Guid id?await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct):null;
        var location=await SensorHistoryScope.ResolveAsync(uow,sensor,device,ct);
        if(location is null||location.AreaId!=areaId||(farmingRowId.HasValue&&location.RowId!=farmingRowId))
            throw AppException.NotFound("Sensor");
        // Only expose scope recorded during authenticated fresh ingest, never backfill old
        // measurements with today's configuration. Same sensor type, unit and row as current.
        var fromUtc=from.UtcDateTime;var toUtc=to.UtcDateTime;var cutoffUtc=cutoff.UtcDateTime;
        var query=db.WaterMeasurements.AsNoTracking().Where(x=>x.SensorId==sensorId&&x.ObservedAreaId==areaId&&
            x.ObservedRowId==location.RowId&&x.ObservedSensorType==sensor.SensorType&&
            x.Unit!=null&&x.Unit!=""&&x.Unit==sensor.Unit&&x.DataQuality=="Realtime"&&
            x.ReceivedAt.HasValue&&x.ReceivedAt<=cutoffUtc&&x.MeasuredAt>=fromUtc&&x.MeasuredAt<toUtc);
        var rows=await query.OrderByDescending(x=>x.MeasuredAt).ThenByDescending(x=>x.Id)
            .Skip((page-1)*pageSize).Take(pageSize+1).Select(x=>new{x.Id,x.SensorId,x.Value,x.Unit,x.MeasuredAt,x.ReceivedAt,
                x.DataQuality,x.ObservedAreaId,x.ObservedRowId,x.ObservedSensorType,x.Source}).ToListAsync(ct);
        var items=rows.Take(pageSize).Select(x=>new{id=x.Id,x.SensorId,x.Value,x.Unit,x.MeasuredAt,x.ReceivedAt,x.DataQuality,
            farmingAreaId=x.ObservedAreaId,farmingRowId=x.ObservedRowId,sensorType=x.ObservedSensorType,
            source=x.Source?.StartsWith("edge:",StringComparison.Ordinal)==true?"edge":"realtime"}).ToArray();
        return Ok(ApiResponse<object>.Ok(new{contract="scoped-sensor-history-v1",farmingAreaId=areaId,sensorId,
            farmingRowId=location.RowId,sensorType=sensor.SensorType,unit=sensor.Unit,sensorActive=sensor.IsActive,
            from=fromUtc,to=toUtc,asOf=cutoffUtc,page,pageSize,hasMore=rows.Count>pageSize,items,
            legacyUnqualifiedExcluded=true,scopeBasis="configured_location_at_ingest",physicalLocationVerified=false,
            databaseSnapshotGuaranteed=false,chemicalResultIncluded=false}));
    }
}
