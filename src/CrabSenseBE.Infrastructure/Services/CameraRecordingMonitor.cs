using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
namespace CrabSenseBE.Infrastructure.Services;

public sealed class CameraRecordingMonitor(IServiceScopeFactory scopes,IConfiguration config,ILogger<CameraRecordingMonitor> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if(!config.GetValue<bool>("CameraRecording:EnableMonitor"))return;
        while(!ct.IsCancellationRequested)
        {
            try
            {
                using var scope=scopes.CreateScope();
                await Sweep(scope.ServiceProvider.GetRequiredService<AppDbContext>(),DateTime.UtcNow,ct);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning(ex,"Recording monitor sweep failed.");}
            await Task.Delay(TimeSpan.FromMinutes(1),ct);
        }
    }
    public static async Task Sweep(AppDbContext db,DateTime now,CancellationToken ct)
    {
        var cameras=await CameraRecordingLibrary.CameraDevices(db).ToListAsync(ct);
        var ids=cameras.Select(x=>x.Id).ToArray();
        var schedules=await (from a in db.CameraScheduleAssignments.AsNoTracking()
            join s in db.CameraSchedules.AsNoTracking() on a.ScheduleId equals s.Id
            where !s.Deleted select new {a.DeviceId,Schedule=s}).ToDictionaryAsync(x=>x.DeviceId,ct);
        var latestRows=await db.CameraRecordingExecutions.AsNoTracking().Where(x=>ids.Contains(x.DeviceId))
            .GroupBy(x=>x.DeviceId).Select(g=>g.OrderByDescending(x=>x.StartedAt).ThenByDescending(x=>x.Id).First()).ToListAsync(ct);
        var latest=latestRows.ToDictionary(x=>x.DeviceId);
        var pending=await db.CameraRecordingExecutions.AsNoTracking().Where(x=>x.Status=="Uploading"&&x.EndedAt<now.AddMinutes(-5))
            .Select(x=>x.DeviceId).Distinct().ToListAsync(ct);
        var agents=await db.CameraRecordingAgents.AsNoTracking().ToDictionaryAsync(x=>x.DeviceId,ct);
        var slots=await db.CameraScheduleSlots.AsNoTracking().ToListAsync(ct);
        var kinds=new[]{"camera_recording_failure","camera_recording_stalled","camera_storage_limit","camera_recording_missed","camera_agent_offline"};
        var alerts=await db.Alerts.Where(a=>a.SourceType=="Device"&&a.IncidentType!=null&&kinds.Contains(a.IncidentType)&&
            (a.Status==AlertStatus.Active||a.Status==AlertStatus.Acknowledged||a.Status==AlertStatus.InProgress)).ToListAsync(ct);
        foreach(var camera in cameras)
        {
            schedules.TryGetValue(camera.Id,out var assignment);latest.TryGetValue(camera.Id,out var execution);
            var failed=execution is not null&&execution.Status=="Failed"&&assignment?.Schedule.Id==execution.ScheduleId;
            var stalled=(execution is not null&&execution.Status=="Recording"&&execution.LastHeartbeatAt<now.AddSeconds(-90))||pending.Contains(camera.Id);
            var quota=false;
            if(assignment is not null&&CameraRecordingRetentionService.LimitBytes(assignment.Schedule) is long limit)
                quota=await CameraRecordingRetentionService.UsedBytes(db,camera.Id,ct)>=limit;
            var due=false;
            if(assignment is not null&&camera.Status!=DeviceStatus.Maintenance&&(assignment.Schedule.UpdatedAt??assignment.Schedule.CreatedAt)<now.AddMinutes(-3))
            {
                var windows=slots.Where(x=>x.ScheduleId==assignment.Schedule.Id).ToArray();
                due=CameraScheduleClock.IsDue(assignment.Schedule,windows,now)&&CameraScheduleClock.IsDue(assignment.Schedule,windows,now.AddMinutes(-3));
            }
            agents.TryGetValue(camera.Id,out var agent);
            var agentOffline=due&&agent is not null&&agent.Enabled&&agent.KioskId.HasValue&&(!agent.LastSeenAt.HasValue||agent.LastSeenAt<now.AddSeconds(-90));
            var missed=due&&!quota&&(execution is null||execution.LastHeartbeatAt<now.AddMinutes(-3));
            var issues=new[]{(kinds[0],failed,"Recording attempt failed"),(kinds[1],stalled,"Recording heartbeat or upload stalled"),(kinds[2],quota,"Camera storage limit reached"),(kinds[3],missed,"No recent recording evidence during scheduled window"),(kinds[4],agentOffline,"Recording agent heartbeat missing")};
            foreach(var (kind,active,message) in issues)
            {
                var source=camera.Id.ToString("N");
                var matching=alerts.Where(x=>x.SourceId==source&&x.IncidentType==kind).ToList();
                var current=matching.FirstOrDefault(x=>x.FarmingAreaId==camera.FarmingAreaId);
                foreach(var alert in matching.Where(x=>!active||x!=current))
                {alert.Status=AlertStatus.Resolved;alert.ResolvedAt=now;alert.ResolutionReason="Recording monitor condition no longer observed";}
                if(!active)continue;
                if(current==null)
                    db.Alerts.Add(new Alert{FarmingAreaId=camera.FarmingAreaId,SourceType="Device",SourceId=source,IncidentType=kind,
                        Message=$"{camera.DeviceCode}: {message}",Severity=AlertSeverity.Warning,Status=AlertStatus.Active,CreatedAt=now,LastOccurredAt=now});
                else if(current.LastOccurredAt==null||current.LastOccurredAt<now.AddMinutes(-5))current.LastOccurredAt=now;
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
