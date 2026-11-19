using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Infrastructure.Services;

public sealed class CameraRecordingRetentionService(AppDbContext db,CameraRecordingLibrary library,ILogger<CameraRecordingRetentionService> logger)
{
    public record RetentionPreview(int Count,long Bytes,int RetentionDays);
    public record CleanupResult(int Deleted,int Failed,bool MoreRemaining);
    private IQueryable<MediaAsset> Candidates(CameraSchedule schedule)
    {
        var cutoff=DateTime.UtcNow.AddDays(-schedule.RetentionDays);
        // Only private technical videos from completed executions of this schedule, still assigned to this camera.
        // Never clean farm/AI media, shared assets, unassigned cameras or videos without execution evidence.
        return CameraRecordingLibrary.TechnicalVideos(db).Where(m=>!m.IsShared &&
            db.CameraScheduleAssignments.Any(a=>a.ScheduleId==schedule.Id&&a.DeviceId==m.DeviceId) &&
            db.CameraRecordingExecutions.Any(e=>e.ScheduleId==schedule.Id&&e.Status=="Completed"&&
                e.DeviceId==m.DeviceId&&e.MediaAssetId==m.Id&&e.EndedAt<cutoff));
    }
    public async Task<RetentionPreview> Preview(CameraSchedule schedule,CancellationToken ct)
    {
        var q=Candidates(schedule);return new(await q.CountAsync(ct),await q.SumAsync(x=>(long?)x.SizeBytes,ct)??0,schedule.RetentionDays);
    }
    public async Task<CleanupResult> Cleanup(Guid scheduleId,int expectedVersion,Guid actor,bool automatic,CancellationToken ct)
    {
        var schedule=await db.CameraSchedules.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==scheduleId&&!x.Deleted,ct)
            ??throw AppException.NotFound("Camera schedule");
        if(schedule.Version!=expectedVersion)throw AppException.Conflict("Schedule changed; reload retention policy.");
        if(automatic&&!schedule.AutoDeleteExpired)return new(0,0,false);
        var ids=await Candidates(schedule).OrderBy(x=>x.CreatedAt).Select(x=>x.Id).Take(101).ToListAsync(ct);
        int deleted=0,failed=0;
        foreach(var id in ids.Take(100))
        {
            var current=await db.CameraSchedules.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==scheduleId&&!x.Deleted,ct);
            if(current is null||current.Version!=expectedVersion||(automatic&&!current.AutoDeleteExpired))break;
            if(!await Candidates(current).AnyAsync(x=>x.Id==id,ct))continue;
            try{await library.DeleteAsync(id,actor,null,ct);deleted++;}
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception ex){failed++;logger.LogWarning(ex,"Retention cleanup failed for media {MediaId}",id);db.ChangeTracker.Clear();}
        }
        return new(deleted,failed,ids.Count>100);
    }
    public static async Task<long> UsedBytes(AppDbContext db,Guid deviceId,CancellationToken ct)
        =>await CameraRecordingLibrary.TechnicalVideos(db).Where(x=>x.DeviceId==deviceId).SumAsync(x=>(long?)x.SizeBytes,ct)??0;
    public static long? LimitBytes(CameraSchedule schedule)=>schedule.MaxStorageGb.HasValue?(long)schedule.MaxStorageGb.Value*1_000_000_000L:null;
}

/// <summary>Opt-in scheduled deletion. Requires both server switch and a real active System Administrator audit actor.</summary>
public sealed class CameraRecordingRetentionWorker(IServiceScopeFactory scopes,IConfiguration config,
    ILogger<CameraRecordingRetentionWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if(!config.GetValue<bool>("CameraRecording:EnableRetentionWorker"))return;
        if(!Guid.TryParse(config["CameraRecording:RetentionActorId"],out var actor))
        {logger.LogWarning("Camera retention worker disabled: audit actor not configured.");return;}
        while(!ct.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();
                var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if(!await db.Set<AppUser>().AnyAsync(x=>x.Id==actor&&x.IsActive&&x.Role==UserRole.SystemAdministrator,ct))
                {logger.LogWarning("Camera retention worker disabled: invalid audit actor.");return;}
                var policies=await db.CameraSchedules.AsNoTracking().Where(x=>!x.Deleted&&x.AutoDeleteExpired)
                    .OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Version}).ToListAsync(ct);
                var service=scope.ServiceProvider.GetRequiredService<CameraRecordingRetentionService>();
                foreach(var p in policies)
                {
                    try{await service.Cleanup(p.Id,p.Version,actor,true,ct);}
                    catch(AppException ex){logger.LogWarning("Retention policy {ScheduleId} not applied ({Status}).",p.Id,ex.StatusCode);}
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning(ex,"Camera retention sweep failed.");}
            await Task.Delay(TimeSpan.FromHours(1),ct);
        }
    }
}
