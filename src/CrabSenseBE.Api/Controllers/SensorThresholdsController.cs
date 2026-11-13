using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

public record SensorThresholdOverride(decimal? MinThreshold, decimal? MaxThreshold);

// Legacy per-sensor thresholds must remain manageable by the assigned farm, never System Admin.
[ApiController]
[Route("api/sensors/{id:guid}/thresholds")]
[Authorize(Roles=AppRoles.FarmPortal)]
public sealed class SensorThresholdsController(AppDbContext db,IUnitOfWork uow,ICurrentUserService currentUser):ControllerBase
{
    private async Task<Sensor> Scoped(Guid id,bool manage,CancellationToken ct)
    {
        var s=await db.Sensors.FindAsync([id],ct)??throw AppException.NotFound("Sensor");
        var areas=new HashSet<Guid>();
        if(s.FarmingAreaId is Guid a)areas.Add(a);
        if(s.WaterSystemId is Guid ws)
        {
            var water=await db.WaterSystems.FindAsync([ws],ct)??throw AppException.Conflict("Sensor water system missing.");
            if(water.FarmingAreaId is Guid x)areas.Add(x);
        }
        if(s.FarmingRowId is Guid rid)
        {
            var row=await db.FarmingRows.FindAsync([rid],ct)??throw AppException.Conflict("Sensor row missing.");
            areas.Add(row.FarmingAreaId);
        }
        if(s.DeviceId is Guid did)
        {
            var device=await db.Devices.FindAsync([did],ct)??throw AppException.Conflict("Sensor controller missing.");
            if(device.FarmingAreaId is Guid x)areas.Add(x);
            if(device.FarmingRowId is Guid dr)
            {
                var row=await db.FarmingRows.FindAsync([dr],ct)??throw AppException.Conflict("Controller row missing.");
                areas.Add(row.FarmingAreaId);
            }
        }
        if(areas.Count!=1)throw AppException.Forbidden("Sensor must have one unambiguous farming area.");
        await new FarmAreaScope(uow,currentUser).EnsureAsync(areas.Single(),manage,ct);
        return s;
    }
    [HttpGet]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct)
    {
        var s=await Scoped(id,false,ct);return Ok(ApiResponse<object>.Ok(new{s.Id,s.MinThreshold,s.MaxThreshold}));
    }
    [HttpPut]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Put(Guid id,SensorThresholdOverride req,CancellationToken ct)
    {
        var s=await Scoped(id,true,ct);
        if(req.MinThreshold.HasValue!=req.MaxThreshold.HasValue||req.MinThreshold.HasValue&&req.MinThreshold>=req.MaxThreshold)
            throw AppException.BadRequest("Provide both min and max with min < max, or both null to use farm thresholds.");
        s.MinThreshold=req.MinThreshold;s.MaxThreshold=req.MaxThreshold;
        db.OperationLogs.Add(new OperationLog{UserId=currentUser.UserId,Action="UpdateSensorThresholds",EntityType="Sensor",EntityId=id,
            IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString()});
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{s.Id,s.MinThreshold,s.MaxThreshold}));
    }
}
