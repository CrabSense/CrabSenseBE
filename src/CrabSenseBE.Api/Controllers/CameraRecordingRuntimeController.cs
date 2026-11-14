using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/cameras/{deviceId:guid}/recording-runtime")]
public sealed class CameraRecordingRuntimeController(AppDbContext db, IConfiguration config, IMediaStorageService? storage = null) : ControllerBase
{
    public record StartRequest(Guid ExecutionId, Guid ScheduleId, int ScheduleVersion);
    public record FailureRequest(Guid ExecutionId, Guid ScheduleId, int ScheduleVersion, string Error);
    public record FinishRequest(string Status, string? Error, Guid? MediaAssetId);
    private static IActionResult Result(object value) => new OkObjectResult(ApiResponse<object>.Ok(value));
    private async Task Authenticate(Guid deviceId,CancellationToken ct)
    {
        var assignment=await db.CameraRecordingAgents.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==deviceId,ct);
        if(assignment!=null)
        {
            if(!assignment.Enabled||!assignment.KioskId.HasValue)throw AppException.Forbidden("Recording agent disabled.");
            var kiosk=await CameraRecordingKioskAuth.Require(db,Request,ct);
            if(kiosk.Id!=assignment.KioskId||!await db.Devices.AnyAsync(x=>x.Id==deviceId&&x.FarmingAreaId==kiosk.FarmingAreaId,ct))
                throw AppException.Forbidden("Camera not assigned to this Kiosk.");
            return;
        }
        // Legacy per-camera key only for cameras without a managed assignment.
        // Dedicated per-camera secret, provisioned outside the API; no shared fleet key or user JWT fallback.
        var expected=config[$"CameraRecording:AgentKeys:{deviceId:D}"];
        var actual=Request.Headers["X-Camera-Agent-Key"].FirstOrDefault();
        if(string.IsNullOrWhiteSpace(expected)||expected.Length<32||string.IsNullOrWhiteSpace(actual)||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),Encoding.UTF8.GetBytes(actual)))
            throw AppException.Unauthorized();
    }
    private async Task<Device> Camera(Guid id,CancellationToken ct) =>
        await CameraRecordingLibrary.CameraDevices(db).SingleOrDefaultAsync(x=>x.Id==id,ct)
        ?? throw AppException.NotFound("Camera");
    private async Task<CameraSchedule?> Schedule(Guid id,CancellationToken ct) =>
        await (from a in db.CameraScheduleAssignments join s in db.CameraSchedules on a.ScheduleId equals s.Id
            where a.DeviceId==id&&!s.Deleted select s).SingleOrDefaultAsync(ct);
    private async Task<bool> Due(CameraSchedule s,CancellationToken ct) =>
        CameraSchedulesController.IsDue(s,await db.CameraScheduleSlots.Where(x=>x.ScheduleId==s.Id).ToListAsync(ct),DateTime.UtcNow);
    private async Task<CameraRecordingExecution> Execution(Guid deviceId,Guid executionId,CancellationToken ct) =>
        await db.CameraRecordingExecutions.SingleOrDefaultAsync(x=>x.DeviceId==deviceId&&x.Id==executionId,ct)
        ?? throw AppException.NotFound("Recording execution");
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException){throw AppException.Conflict("Concurrent execution change; reload.");}
    }
    [AllowAnonymous]
    [HttpGet("configuration")]
    public async Task<IActionResult> Configuration(Guid deviceId,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);var camera=await Camera(deviceId,ct);var s=await Schedule(deviceId,ct);
        if(s is null) return Result(new { deviceId, shouldRecord=false, reason="NoSchedule" });
        var used=await CameraRecordingRetentionService.UsedBytes(db,deviceId,ct);var limit=CameraRecordingRetentionService.LimitBytes(s);
        var quotaReached=limit.HasValue&&used>=limit.Value;
        var due=camera.Status!=DeviceStatus.Maintenance&&!quotaReached&&await Due(s,ct);
        return Result(new {deviceId,scheduleId=s.Id,scheduleVersion=s.Version,shouldRecord=due,usedBytes=used,limitBytes=limit,reason=quotaReached?"StorageLimitReached":null,
            s.Mode,s.Timezone,s.RetentionDays,s.VideoQuality,s.Fps,s.MaxStorageGb,
            streamUrl=CameraConfiguration.SafeUrl(camera.StreamUrl), leaseSeconds=90});
    }
    [AllowAnonymous]
    [HttpPost("executions")]
    public async Task<IActionResult> Start(Guid deviceId,StartRequest r,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);var camera=await Camera(deviceId,ct);
        if(r.ExecutionId==Guid.Empty) throw AppException.BadRequest("ExecutionId required.");
        var existing=await db.CameraRecordingExecutions.SingleOrDefaultAsync(x=>x.Id==r.ExecutionId,ct);
        if(existing!=null)
        {
            if(existing.DeviceId!=deviceId||existing.ScheduleId!=r.ScheduleId||existing.ScheduleVersion!=r.ScheduleVersion)
                throw AppException.Conflict("Execution identity mismatch.");
            return Result(existing);
        }
        var s=await Schedule(deviceId,ct);
        if(s is null||s.Id!=r.ScheduleId||s.Version!=r.ScheduleVersion||camera.Status==DeviceStatus.Maintenance||!await Due(s,ct))
            throw AppException.Conflict("Schedule is not currently effective.");
        var limit=CameraRecordingRetentionService.LimitBytes(s);
        if(limit.HasValue&&await CameraRecordingRetentionService.UsedBytes(db,deviceId,ct)>=limit.Value)throw AppException.Conflict("Camera storage limit reached.");
        var now=DateTime.UtcNow;
        var active=await db.CameraRecordingExecutions.Where(x=>x.DeviceId==deviceId&&x.Status=="Recording").ToListAsync(ct);
        if(active.Any(x=>x.LastHeartbeatAt>=now.AddSeconds(-90))) throw AppException.Conflict("Camera already has an active execution.");
        foreach(var old in active){old.Status="Failed";old.EndedAt=now;old.Error="Agent heartbeat expired.";}
        var row=new CameraRecordingExecution{Id=r.ExecutionId,DeviceId=deviceId,ScheduleId=s.Id,ScheduleVersion=s.Version,
            StartedAt=now,LastHeartbeatAt=now,Status="Recording"};
        // Save expired rows before inserting the new lease; transaction preserves atomicity.
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        await Save(ct);db.CameraRecordingExecutions.Add(row);await Save(ct);if(tx!=null) await tx.CommitAsync(ct);
        return Result(row);
    }
    [AllowAnonymous]
    [HttpPost("failures")]
    public async Task<IActionResult> Failure(Guid deviceId,FailureRequest r,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);await Camera(deviceId,ct);
        if(r.ExecutionId==Guid.Empty||string.IsNullOrWhiteSpace(r.Error)||r.Error.Length>1000)
            throw AppException.BadRequest("Invalid failure report.");
        var existing=await db.CameraRecordingExecutions.SingleOrDefaultAsync(x=>x.Id==r.ExecutionId,ct);
        if(existing!=null)
        {
            if(existing.DeviceId!=deviceId||existing.ScheduleId!=r.ScheduleId||existing.ScheduleVersion!=r.ScheduleVersion)
                throw AppException.Conflict("Execution identity mismatch.");
            return Result(existing);
        }
        var s=await Schedule(deviceId,ct);
        if(s is null||s.Id!=r.ScheduleId||s.Version!=r.ScheduleVersion)
            throw AppException.Conflict("Schedule changed.");
        var now=DateTime.UtcNow;
        var row=new CameraRecordingExecution{Id=r.ExecutionId,DeviceId=deviceId,ScheduleId=s.Id,ScheduleVersion=s.Version,
            Status="Failed",StartedAt=now,EndedAt=now,LastHeartbeatAt=now,Error=r.Error.Trim()};
        db.CameraRecordingExecutions.Add(row);await Save(ct);return Result(row);
    }
    [AllowAnonymous]
    [HttpPost("executions/{executionId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid deviceId,Guid executionId,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);var camera=await Camera(deviceId,ct);var row=await Execution(deviceId,executionId,ct);
        var now=DateTime.UtcNow;
        if(row.Status!="Recording"||row.LastHeartbeatAt<now.AddSeconds(-90)) throw AppException.Conflict("Execution lease expired or closed.");
        var s=await Schedule(deviceId,ct);
        var due=s!=null&&s.Id==row.ScheduleId&&s.Version==row.ScheduleVersion&&camera.Status!=DeviceStatus.Maintenance&&await Due(s,ct);
        if(due){row.LastHeartbeatAt=now;await Save(ct);}
        return Result(new {executionId,shouldContinue=due,leaseSeconds=90});
    }
    [AllowAnonymous]
    [HttpPost("executions/{executionId:guid}/captured")]
    public async Task<IActionResult> Captured(Guid deviceId,Guid executionId,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);var row=await Execution(deviceId,executionId,ct);
        if(row.Status is "Uploading" or "Completed")return Result(row);
        if(row.Status!="Recording"&&!(row.Status=="Failed"&&row.Error=="Agent heartbeat expired."))throw AppException.Conflict("Execution already closed.");
        row.Status="Uploading";row.Error=null;row.EndedAt??=DateTime.UtcNow;await Save(ct);return Result(row);
    }
    [AllowAnonymous]
    [HttpPut("executions/{executionId:guid}/finish")]
    public async Task<IActionResult> Finish(Guid deviceId,Guid executionId,FinishRequest r,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);var row=await Execution(deviceId,executionId,ct);
        if(r.Status is not ("Completed" or "Failed" or "Stopped")||r.Error?.Length>1000||
            (r.Status=="Completed"&&!r.MediaAssetId.HasValue)|| (r.Status=="Failed"&&string.IsNullOrWhiteSpace(r.Error)))
            throw AppException.BadRequest("Invalid execution outcome.");
        if(row.Status is not ("Recording" or "Uploading"))
        {
            if(row.Status==r.Status&&row.MediaAssetId==r.MediaAssetId) return Result(row);
            throw AppException.Conflict("Execution already closed.");
        }
        if(r.MediaAssetId.HasValue&&!await CameraRecordingLibrary.TechnicalVideos(db).AnyAsync(x=>x.Id==r.MediaAssetId&&x.DeviceId==deviceId,ct))
            throw AppException.BadRequest("Video does not belong to this camera.");
        row.Status=r.Status;row.EndedAt??=DateTime.UtcNow;row.Error=r.Error;row.MediaAssetId=r.MediaAssetId;
        await Save(ct);return Result(row);
    }
    [AllowAnonymous]
    [HttpPost("executions/{executionId:guid}/video")]
    [RequestSizeLimit(60_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit=60_000_000)]
    public async Task<IActionResult> Upload(Guid deviceId,Guid executionId,IFormFile file,CancellationToken ct)
    {
        await Authenticate(deviceId,ct);
        await using var tx=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(tx!=null) _=await db.Devices.FromSqlInterpolated($"SELECT * FROM be.\"Devices\" WHERE \"Id\"={deviceId} FOR UPDATE").ToListAsync(ct);
        var row=tx is null?await Execution(deviceId,executionId,ct):
            (await db.CameraRecordingExecutions.FromSqlInterpolated($"SELECT * FROM be.\"CameraRecordingExecutions\" WHERE \"Id\"={executionId} AND \"DeviceId\"={deviceId} FOR UPDATE").ToListAsync(ct)).SingleOrDefault()
                ??throw AppException.NotFound("Recording execution");
        if(row.Status=="Completed") return Result(row);
        if(row.Status!="Uploading"&&(row.Status!="Recording"||row.LastHeartbeatAt<DateTime.UtcNow.AddSeconds(-90)))
            throw AppException.Conflict("Execution closed or expired.");
        if(storage is null) throw new AppException("Media storage unavailable.",503);
        if(file is null||file.Length<12||file.Length>50_000_000) throw AppException.BadRequest("MP4 size must be between 12 bytes and 50 MB.");
        var schedule=await db.CameraSchedules.AsNoTracking().SingleAsync(x=>x.Id==row.ScheduleId,ct);
        var limit=CameraRecordingRetentionService.LimitBytes(schedule);
        if(limit.HasValue&&file.Length>limit.Value-await CameraRecordingRetentionService.UsedBytes(db,deviceId,ct))throw AppException.Conflict("Camera storage limit reached.");
        await using var input=file.OpenReadStream();
        var header=new byte[12];await input.ReadExactlyAsync(header,ct);
        if(Encoding.ASCII.GetString(header,4,4)!="ftyp") throw AppException.BadRequest("MP4 container required.");
        if(!input.CanSeek) throw AppException.BadRequest("Seekable upload required.");
        input.Position=0;
        var filename=$"camera-{deviceId:N}-{executionId:N}.mp4";
        var uploaded=await storage.UploadAsync(input,filename,"video/mp4","video",ct,publicRead:false);
        var asset=new MediaAsset{DeviceId=deviceId,Category="video",FileName=filename,ContentType="video/mp4",
            SizeBytes=uploaded.SizeBytes,Provider=storage.ProviderName,StorageKey=uploaded.StorageKey,
            RelatedEntityType="Device",RelatedEntityId=deviceId,IsShared=false};
        db.MediaAssets.Add(asset);row.MediaAssetId=asset.Id;row.Status="Completed";row.EndedAt??=DateTime.UtcNow;
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch(DbUpdateException)
        {
            if(tx!=null)await tx.RollbackAsync(CancellationToken.None);
            try{await storage.DeleteAsync(uploaded.StorageKey,CancellationToken.None);}catch{ /* provider reconciliation required */ }
            throw AppException.Conflict("Upload metadata not confirmed; retry after reconciliation.");
        }
        if(tx!=null)await tx.CommitAsync(CancellationToken.None);
        return Result(row);
    }
    [Authorize(Roles=AppRoles.Platform)]
    [HttpGet("/api/cameras/{deviceId:guid}/recording-status")]
    public async Task<IActionResult> Status(Guid deviceId,CancellationToken ct)
    {
        var camera=await Camera(deviceId,ct);var s=await Schedule(deviceId,ct);
        var row=await db.CameraRecordingExecutions.AsNoTracking().Where(x=>x.DeviceId==deviceId).OrderByDescending(x=>x.StartedAt).FirstOrDefaultAsync(ct);
        var fresh=row?.Status=="Recording"&&row.LastHeartbeatAt>=DateTime.UtcNow.AddSeconds(-90);
        var runtimeStatus=fresh?"Recording":row?.Status=="Recording"?"Unknown":row?.Status??"NotIntegrated";
        var usedBytes=await CameraRecordingRetentionService.UsedBytes(db,deviceId,ct);
        var limitBytes=s is null?null:CameraRecordingRetentionService.LimitBytes(s);
        var slots=s is null?new List<CameraScheduleSlot>():await db.CameraScheduleSlots.Where(x=>x.ScheduleId==s.Id).ToListAsync(ct);
        var now=DateTime.UtcNow;
        var agent=await db.CameraRecordingAgents.AsNoTracking().SingleOrDefaultAsync(x=>x.DeviceId==deviceId,ct);
        var nextRecordingAt=s is null||camera.Status==DeviceStatus.Maintenance?null:CameraSchedulesController.NextDue(s,slots,now);
        return Result(new {deviceId,scheduleId=s?.Id,scheduleName=s?.Name,scheduledNow=s!=null&&camera.Status!=DeviceStatus.Maintenance&&CameraSchedulesController.IsDue(s,slots,now),nextRecordingAt,runtimeStatus,lastExecution=row,usedBytes,limitBytes,agentConfigured=agent is null?!string.IsNullOrWhiteSpace(config[$"CameraRecording:AgentKeys:{deviceId:D}"]):agent.Enabled&&agent.KioskId.HasValue,agentLastSeenAt=agent?.LastSeenAt,pendingClips=agent?.PendingClips,kioskId=agent?.KioskId});
    }
}
