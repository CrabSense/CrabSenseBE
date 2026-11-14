using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Api.Controllers;

internal static class CameraRecordingKioskAuth
{
    internal static async Task<FarmKiosk> Require(AppDbContext db,HttpRequest request,CancellationToken ct)
    {
        var key=request.Headers["X-Kiosk-Key"].FirstOrDefault()?.Trim();
        if(string.IsNullOrEmpty(key)||key.Length>512)throw AppException.Unauthorized();
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return await (from c in db.KioskCredentials.AsNoTracking()
            join k in db.FarmKiosks.AsNoTracking() on c.KioskId equals k.Id
            where c.SecretHash==hash&&c.RevokedAt==null&&k.Status!="Revoked" select k).FirstOrDefaultAsync(ct)
            ??throw AppException.Unauthorized();
    }
}
[ApiController]
[Route("api/cameras/recording-agents")]
[Authorize(Roles=AppRoles.Platform)]
public sealed class CameraRecordingAgentsController(AppDbContext db):ControllerBase
{
    public record Assignment(Guid? KioskId,bool Enabled,int ExpectedVersion);
    public record AgentHeartbeat(string? WorkerVersion,int PendingClips);
    private static IActionResult Result(object value)=>new OkObjectResult(ApiResponse<object>.Ok(value));
    private async Task<Guid> Actor(CancellationToken ct)
    {
        if(!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)??User.FindFirstValue("sub"),out var id)||
            !await db.AppUsers.AnyAsync(x=>x.Id==id&&x.IsActive&&x.Role==UserRole.SystemAdministrator,ct))
            throw AppException.Forbidden("System administrator required.");
        return id;
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        await Actor(ct);
        var cameras=await CameraRecordingLibrary.CameraDevices(db).OrderBy(x=>x.DeviceCode).ToListAsync(ct);
        var agents=await db.CameraRecordingAgents.AsNoTracking().ToDictionaryAsync(x=>x.DeviceId,ct);
        var kiosks=await db.FarmKiosks.AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
        var now=DateTime.UtcNow;
        return Result(cameras.Select(c=>{
            agents.TryGetValue(c.Id,out var a);
            FarmKiosk? kiosk=null;if(a?.KioskId is Guid k)kiosks.TryGetValue(k,out kiosk);
            var valid=a?.Enabled==true&&kiosk!=null&&kiosk.Status!="Revoked"&&c.FarmingAreaId==kiosk.FarmingAreaId;
            return new {id=c.Id,deviceId=c.Id,c.DeviceCode,c.Name,c.FarmingAreaId,kioskId=a?.KioskId,kioskCode=kiosk?.Code,
                enabled=a?.Enabled??false,version=a?.Version??0,lastSeenAt=a?.LastSeenAt,workerVersion=a?.WorkerVersion,pendingClips=a?.PendingClips??0,
                status=a is null?"Unassigned":!valid?"Disabled":a.LastSeenAt>=now.AddSeconds(-90)?"Online":"Offline"};
        }).ToList());
    }
    [HttpPut("{deviceId:guid}")]
    public async Task<IActionResult> Assign(Guid deviceId,Assignment request,CancellationToken ct)
    {
        var actor=await Actor(ct);
        var camera=await CameraRecordingLibrary.CameraDevices(db).SingleOrDefaultAsync(x=>x.Id==deviceId,ct)??throw AppException.NotFound("Camera");
        if(request.Enabled)
        {
            if(!request.KioskId.HasValue||!await db.FarmKiosks.AnyAsync(k=>k.Id==request.KioskId&&k.Status!="Revoked"&&k.FarmingAreaId==camera.FarmingAreaId,ct))
                throw AppException.BadRequest("Kiosk must belong to the same camera area and not be revoked.");
        }
        var row=await db.CameraRecordingAgents.SingleOrDefaultAsync(x=>x.DeviceId==deviceId,ct);
        if(request.ExpectedVersion!=(row?.Version??0))throw AppException.Conflict("Agent assignment changed; reload.");
        if(row is null){row=new CameraRecordingAgent{DeviceId=deviceId};db.CameraRecordingAgents.Add(row);}
        else row.Version++;
        row.KioskId=request.Enabled?request.KioskId:null;row.Enabled=request.Enabled;row.LastSeenAt=null;row.PendingClips=0;row.WorkerVersion=null;
        db.OperationLogs.Add(new OperationLog{UserId=actor,EntityType="Device",EntityId=deviceId,Action=request.Enabled?"AssignCameraAgent":"RevokeCameraAgent"});
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Agent assignment changed; reload.");}
        return Result(new {deviceId,row.KioskId,row.Enabled,row.Version});
    }
    [AllowAnonymous]
    [HttpGet("assigned")]
    public async Task<IActionResult> Assigned(CancellationToken ct)
    {
        var kiosk=await CameraRecordingKioskAuth.Require(db,Request,ct);
        var ids=await (from a in db.CameraRecordingAgents.AsNoTracking()
            join d in CameraRecordingLibrary.CameraDevices(db) on a.DeviceId equals d.Id
            where a.KioskId==kiosk.Id&&a.Enabled&&d.FarmingAreaId==kiosk.FarmingAreaId
            select new {deviceId=d.Id,a.Version}).ToListAsync(ct);
        return Result(ids);
    }
    [AllowAnonymous]
    [HttpPost("{deviceId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid deviceId,AgentHeartbeat request,CancellationToken ct)
    {
        var kiosk=await CameraRecordingKioskAuth.Require(db,Request,ct);
        if(request.PendingClips is <0 or >1000||request.WorkerVersion?.Length>80)throw AppException.BadRequest("Invalid heartbeat.");
        var row=await db.CameraRecordingAgents.SingleOrDefaultAsync(x=>x.DeviceId==deviceId&&x.KioskId==kiosk.Id&&x.Enabled,ct);
        if(row is null||!await db.Devices.AnyAsync(x=>x.Id==deviceId&&x.FarmingAreaId==kiosk.FarmingAreaId,ct))throw AppException.Forbidden("Camera not assigned.");
        row.LastSeenAt=DateTime.UtcNow;row.WorkerVersion=request.WorkerVersion;row.PendingClips=request.PendingClips;
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw AppException.Conflict("Agent assignment changed.");}
        return Result(new {deviceId,row.Version});
    }
}
