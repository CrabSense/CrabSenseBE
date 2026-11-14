using System.Security.Claims;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

public record SensorTestResult(string DeviceCode, bool Success, DateTimeOffset? MeasuredAt,
    decimal? Value, string? Unit, string? Message = null);

[ApiController]
[Route("api/sensors")]
[Authorize(Roles=AppRoles.Platform)]
public sealed class SensorDiagnosticsController(AppDbContext db) : ControllerBase
{
    private Guid Actor()=>Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)
        ?id:throw AppException.Unauthorized();
    private async Task<Sensor> Require(Guid id,CancellationToken ct)=>await db.Sensors.FindAsync([id],ct)??throw AppException.NotFound("Sensor");

    internal static bool IsCurrent(Sensor s,SensorDiagnosticRun run,DateTime now)=>
        s.IsActive && s.DeviceId==run.DeviceId && s.ConfigurationVersion==run.ConfigurationVersion && run.ExpiresAt>now;
    internal static async Task<bool> CurrentPort(AppDbContext db,Sensor s,Device d,CancellationToken ct)
    {
        var id=s.ControllerPortId??s.BusPortId;
        if(!id.HasValue)return true; // legacy registered sensor, firmware decides how to read it.
        var p=await db.ControllerPorts.FindAsync([id.Value],ct);
        return p!=null&&p.DeviceId==d.Id&&p.IsPresent&&p.ConfigVersion==d.CapabilityVersion
            &&(s.ControllerPortId.HasValue?(p.PortType is "DIGITAL_INPUT" or "ANALOG_INPUT")&&(p.SensorCode==null||p.SensorCode==s.SensorCode)
                :p.PortType is "I2C" or "RS485" or "UART");
    }
    private async Task Expire(Sensor s,CancellationToken ct)
    {
        var pending=await db.SensorDiagnosticRuns.Where(r=>r.SensorId==s.Id&&r.Status=="Pending").ToListAsync(ct);
        var now=DateTime.UtcNow;
        foreach(var run in pending.Where(r=>!IsCurrent(s,r,now)))
        {run.Status=run.ExpiresAt<=now?"Expired":"Cancelled";run.CompletedAt=now;}
        if(db.ChangeTracker.HasChanges())await db.SaveChangesAsync(ct);
    }
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> RequestTest(Guid id,CancellationToken ct)
    {
        var s=await Require(id,ct);
        if(!s.IsActive||s.DeviceId==null)throw AppException.Conflict("Sensor must be active and linked to a controller.");
        var d=await db.Devices.FindAsync([s.DeviceId.Value],ct)??throw AppException.NotFound("Controller");
        if(d.EdgeState is not ("Approved" or "Online")||d.Status==DeviceStatus.Maintenance||string.IsNullOrWhiteSpace(d.CredentialHash))
            throw AppException.Conflict("Controller must be approved with a device-specific Edge V2 credential.");
        if(!await CurrentPort(db,s,d,ct))throw AppException.Conflict("Sensor port is no longer current.");
        await Expire(s,ct);
        var existing=await db.SensorDiagnosticRuns.FirstOrDefaultAsync(r=>r.SensorId==id&&r.Status=="Pending",ct);
        if(existing!=null)return Accepted(ApiResponse<object>.Ok(existing,"Test already pending."));
        var run=new SensorDiagnosticRun{SensorId=id,DeviceId=d.Id,RequestedBy=Actor(),ConfigurationVersion=s.ConfigurationVersion,
            ExpiresAt=DateTime.UtcNow.AddMinutes(2)};
        db.SensorDiagnosticRuns.Add(run);
        db.OperationLogs.Add(new OperationLog{UserId=run.RequestedBy,Action="SensorTestRequested",EntityType="Sensor",EntityId=id,
            Details=run.Id.ToString("N"),IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString()});
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Concurrent test request; reload test status.");}
        return Accepted(ApiResponse<object>.Ok(run,"Test queued; waiting for controller result."));
    }
    [HttpPost("{id:guid}/tests/{testId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id,Guid testId,CancellationToken ct)
    {
        var s=await Require(id,ct);await Expire(s,ct);
        var run=await db.SensorDiagnosticRuns.SingleOrDefaultAsync(r=>r.Id==testId&&r.SensorId==id,ct)??throw AppException.NotFound("Sensor test");
        if(run.Status!="Pending")throw AppException.Conflict("Only pending tests may be cancelled.");
        run.Status="Cancelled";run.CompletedAt=DateTime.UtcNow;
        db.OperationLogs.Add(new OperationLog{UserId=Actor(),Action="CancelSensorTest",EntityType="Sensor",EntityId=id,Details=testId.ToString("N")});
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw AppException.Conflict("Test state changed; reload.");}
        return Ok(ApiResponse<object>.Ok(run));
    }
    [HttpGet("{id:guid}/tests")]
    public async Task<IActionResult> Tests(Guid id,CancellationToken ct)
    {
        var s=await Require(id,ct);await Expire(s,ct);
        return Ok(ApiResponse<object>.Ok(await db.SensorDiagnosticRuns.AsNoTracking().Where(r=>r.SensorId==id)
            .OrderByDescending(r=>r.CreatedAt).Take(100).ToListAsync(ct)));
    }
    [HttpGet("{id:guid}/tests/{testId:guid}")]
    public async Task<IActionResult> Test(Guid id,Guid testId,CancellationToken ct)
    {
        var s=await Require(id,ct);await Expire(s,ct);
        var run=await db.SensorDiagnosticRuns.AsNoTracking().FirstOrDefaultAsync(r=>r.SensorId==id&&r.Id==testId,ct)
            ??throw AppException.NotFound("Sensor test");
        return Ok(ApiResponse<object>.Ok(run));
    }
}

[ApiController]
[Route("api/iot/sensor-tests")]
public sealed class SensorDiagnosticEdgeController(AppDbContext db,IConfiguration configuration,IUnitOfWork uow):ControllerBase
{
    private async Task<Device> Authenticate(string deviceCode,CancellationToken ct)
    {
        var (denied,identity)=await EdgeKeyGuard.CheckDeviceAsync(Request,configuration,uow,deviceCode,ct);
        if(denied!=null||identity==null||identity.IsLegacyCredential||Request.Headers["X-Edge-Protocol"].ToString()!="2")
            throw AppException.Unauthorized("Device-specific Edge V2 credential required.");
        return await db.Devices.FindAsync([identity.DeviceId],ct)??throw AppException.NotFound("Controller");
    }
    [HttpGet("pending")]
    [AllowAnonymous]
    public async Task<IActionResult> Pending([FromQuery]string deviceCode,CancellationToken ct)
    {
        var d=await Authenticate(deviceCode,ct);var now=DateTime.UtcNow;
        var rows=await (from run in db.SensorDiagnosticRuns.AsNoTracking()
            join s in db.Sensors.AsNoTracking() on run.SensorId equals s.Id
            where run.DeviceId==d.Id&&run.Status=="Pending"&&run.ExpiresAt>now&&s.IsActive&&s.DeviceId==d.Id&&s.ConfigurationVersion==run.ConfigurationVersion
            orderby run.CreatedAt
            select new{run,s}).Take(20).ToListAsync(ct);
        var result=new List<object>();
        foreach(var x in rows)
            if(await SensorDiagnosticsController.CurrentPort(db,x.s,d,ct))
                {
                var portId=x.s.ControllerPortId??x.s.BusPortId;
                var port=portId.HasValue?await db.ControllerPorts.FindAsync([portId.Value],ct):null;
                result.Add(new{x.run.Id,x.run.SensorId,x.s.SensorCode,x.s.SensorType,x.s.Unit,x.s.ControllerPortId,x.s.BusPortId,x.s.BusAddress,x.s.BusRegister,
                    portCode=port?.PortCode,portType=port?.PortType,x.run.ConfigurationVersion,x.run.ExpiresAt});
            }
        return Ok(ApiResponse<object>.Ok(result));
    }
    [HttpPut("{testId:guid}/result")]
    [AllowAnonymous]
    public async Task<IActionResult> Result(Guid testId,SensorTestResult req,CancellationToken ct)
    {
        var d=await Authenticate(req.DeviceCode,ct);var now=DateTime.UtcNow;
        var run=await db.SensorDiagnosticRuns.FindAsync([testId],ct)??throw AppException.NotFound("Sensor test");
        if(run.DeviceId!=d.Id)throw AppException.Forbidden("Test belongs to another controller.");
        var s=await db.Sensors.FindAsync([run.SensorId],ct)??throw AppException.NotFound("Sensor");
        if(run.Status!="Pending")throw AppException.Conflict("Test already completed, cancelled or expired.");
        if(!SensorDiagnosticsController.IsCurrent(s,run,now)||!await SensorDiagnosticsController.CurrentPort(db,s,d,ct))
            throw AppException.Conflict("Test expired or sensor configuration changed.");
        if(req.Message?.Length>500||req.Unit?.Length>32)throw AppException.BadRequest("Invalid result metadata.");
        if(req.Success&&(req.Value==null||req.MeasuredAt==null||req.MeasuredAt.Value.UtcDateTime<run.CreatedAt.AddSeconds(-5)
            ||req.MeasuredAt.Value.UtcDateTime>now.AddSeconds(5)
            ||s.Unit!=null&&!string.Equals(s.Unit,req.Unit,StringComparison.OrdinalIgnoreCase)))
            throw AppException.BadRequest("Successful test requires a fresh measured value and matching unit.");
        run.Status=req.Success?"Succeeded":"Failed";run.CompletedAt=now;run.MeasuredAt=req.MeasuredAt?.UtcDateTime;
        run.Value=req.Value;run.Unit=req.Unit;run.ResultMessage=req.Message;
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw AppException.Conflict("Test state changed; reload.");}
        // Diagnostic samples never enter the production measurement/automation stream.
        return Ok(ApiResponse<object>.Ok(run));
    }
}
