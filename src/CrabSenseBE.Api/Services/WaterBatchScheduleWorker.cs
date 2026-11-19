using System.Text.Json;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Services;

/// <summary>Durable planning only. Never starts pumps or claims chemical tests ran.</summary>
public sealed class WaterBatchScheduleWorker(IServiceScopeFactory scopes,ILogger<WaterBatchScheduleWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30),ct);
                using var scan=scopes.CreateScope();
                if(scan.ServiceProvider.GetRequiredService<IDatabaseBackupService>().IsMaintenanceMode)continue;
                var db=scan.ServiceProvider.GetRequiredService<AppDbContext>();
                var now=DateTime.UtcNow;
                var due=await db.WaterAnalysisSchedules.AsNoTracking().Where(x=>x.Enabled&&x.NextRunAt<=now).OrderBy(x=>x.NextRunAt).Take(50).ToListAsync(ct);
                foreach(var item in due)
                {
                    try{await Plan(item,now,ct);}
                    catch(Exception ex) when(ex is not OperationCanceledException){logger.LogWarning(ex,"Water batch schedule {ScheduleId} failed",item.Id);}
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogWarning(ex,"Water batch scan failed");}
        }
    }
    private async Task Plan(WaterAnalysisSchedule item,DateTime now,CancellationToken ct)
    {
        using var scope=scopes.CreateScope();
        if(scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>().IsMaintenanceMode)return;
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var next=WaterBatchPlanning.Next(item.NextRunAt,now,item.IntervalMinutes);
        var claimed=await db.WaterAnalysisSchedules.Where(x=>x.Id==item.Id&&x.Enabled&&x.NextRunAt==item.NextRunAt)
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.NextRunAt,next).SetProperty(x=>x.LastAttemptAt,now),ct);
        if(claimed!=1)return;
        var targets=WaterBatchPlanning.Normalize(JsonSerializer.Deserialize<WaterBatchPlanning.Target[]>(item.TargetsJson,WaterBatchPlanning.Json),true);
        string outcome;
        if(!await WaterBatchPlanning.ProfilesReady(db,item.FarmingAreaId,targets,now,ct))outcome="blocked_calibration";
        else if(await db.WaterAnalysisBatches.AnyAsync(x=>x.FarmingAreaId==item.FarmingAreaId&&x.Status=="waiting_samples",ct)||
            await db.WaterAnalysisRuns.AnyAsync(x=>x.FarmingAreaId==item.FarmingAreaId&&(x.Status=="running"||x.Status=="stopping"),ct))outcome="skipped_busy";
        else
        {
            var batch=WaterBatchPlanning.New(item.FarmingAreaId,"scheduled",targets,item.SampleSource,now,scheduleId:item.Id,scheduledFor:item.NextRunAt);
            db.WaterAnalysisBatches.Add(batch);
            await WaterBatchPlanning.AddChannels(db,batch,targets,now,ct);
            outcome="waiting_samples";
        }
        await db.SaveChangesAsync(ct);
        await db.WaterAnalysisSchedules.Where(x=>x.Id==item.Id).ExecuteUpdateAsync(u=>u.SetProperty(x=>x.LastOutcome,outcome),ct);
        await tx.CommitAsync(ct);
    }
}
