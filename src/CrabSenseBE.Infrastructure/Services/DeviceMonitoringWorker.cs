using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace CrabSenseBE.Infrastructure.Services;

public sealed class DeviceMonitoringWorker(IServiceScopeFactory scopes,IConfiguration config,ILogger<DeviceMonitoringWorker> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken ct){
    if(!config.GetValue("DeviceMonitoring:EnableHistory",true))return;
    await Task.Delay(TimeSpan.FromSeconds(45),ct);
    while(!ct.IsCancellationRequested){
        try{using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await SampleAsync(db,DateTime.UtcNow,ct);}
        catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
        catch(Exception ex){logger.LogWarning(ex,"Device monitoring history sample failed");}
        await Task.Delay(TimeSpan.FromMinutes(1),ct);
    }
 }
 public static async Task SampleAsync(AppDbContext db,DateTime now,CancellationToken ct){
    // A transaction-level advisory lock serializes multiple API instances without locking telemetry writes.
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74638112015)",ct);
    var rows=await new DeviceMonitoringReader(db).ReadAsync(now,ct,false);
    var states=await db.DeviceMonitoringStates.ToDictionaryAsync(x=>new ValueTuple<string,Guid>(x.SourceType,x.SourceId),ct);
    foreach(var row in rows){
        var key=(row.Type,row.Id);var state=states.GetValueOrDefault(key);
        if(state!=null&&state.ObservedAt>=now)continue;
        if(state==null||state.Status!=row.ConnectionStatus||state.FarmingAreaId!=row.FarmingAreaId||now-state.ObservedAt>TimeSpan.FromMinutes(3)){
            var gap=state!=null&&now-state.ObservedAt>TimeSpan.FromMinutes(3);
            db.DeviceConnectionEvents.Add(new(){SourceType=row.Type,SourceId=row.Id,FarmingAreaId=row.FarmingAreaId,
                PreviousStatus=gap?null:state?.Status,NewStatus=row.ConnectionStatus,OccurredAt=now,LastSeenAt=row.LastSeenAt,PreviousObservedAt=state?.ObservedAt,
                ReasonCode=state==null?"initial_observation":gap?"observation_gap":state.FarmingAreaId!=row.FarmingAreaId?"area_changed":row.ReasonCode??"status_changed"});
        }
        if(state==null){state=new(){SourceType=row.Type,SourceId=row.Id};db.DeviceMonitoringStates.Add(state);}
        state.Status=row.ConnectionStatus;state.ObservedAt=now;state.FarmingAreaId=row.FarmingAreaId;
    }
    var keys=rows.Select(x=>(x.Type,x.Id)).ToHashSet();
    foreach(var state in states.Values.Where(x=>x.ObservedAt<now&&!keys.Contains((x.SourceType,x.SourceId)))){
        db.DeviceConnectionEvents.Add(new(){SourceType=state.SourceType,SourceId=state.SourceId,FarmingAreaId=state.FarmingAreaId,
            PreviousStatus=state.Status,NewStatus="Removed",ReasonCode="source_removed",OccurredAt=now,PreviousObservedAt=state.ObservedAt});
        db.DeviceMonitoringStates.Remove(state);
    }
    await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
 }
}
