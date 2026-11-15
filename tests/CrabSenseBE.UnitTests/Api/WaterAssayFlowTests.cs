using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using Xunit;
namespace CrabSenseBE.UnitTests.Api;
public class WaterAssayFlowTests
{
    private static WaterAssayChannel Channel(string analyte,DateTime now)
    {
        var p=WaterAssayCatalog.For(analyte);
        var protocol=new ColorProtocol(100,100,new(0,0,20,20),new(50,50,20,20),"G",p.ReactionSeconds,20,5,"reference",10,20);
        return WaterAssayCatalog.New(Guid.NewGuid(),new WaterColorCalibration{Analyte=analyte,ProtocolJson=JsonSerializer.Serialize(protocol,WaterBatchPlanning.Json)},now);
    }
    [Fact] public void CatalogSeparatesReferencesAndPowder()
    {
        var p=WaterAssayCatalog.For("NO3");
        Assert.Equal(1m/3m,p.Steps.Single(x=>x.Code=="powder").Amount);
        Assert.Equal(10,p.Steps.Single(x=>x.Code=="mix_powder").Seconds);
        Assert.Empty(p.References.Single().Levels);
        Assert.Equal(2,WaterAssayCatalog.For("NO2").References.Length);
        Assert.All(WaterAssayCatalog.All(),x=>Assert.False(x.HardwareExecutable));
    }
    [Fact] public void EnforcesTimerCaptureAndCleanup()
    {
        var now=new DateTime(2026,11,15,0,0,0,DateTimeKind.Utc);var actor=Guid.NewGuid();
        var c=Channel("NO2",now);
        Assert.Throws<AppException>(()=>WaterAssayFlow.Confirm(c,"sample",now,actor));
        while(WaterAssayFlow.Current(c)!="wait") WaterAssayFlow.Confirm(c,WaterAssayFlow.Current(c),now,actor);
        Assert.Equal(now.AddSeconds(300),c.ReactionReadyAt);
        Assert.Throws<AppException>(()=>WaterAssayFlow.Confirm(c,"wait",now,actor));
        now=now.AddSeconds(300);WaterAssayFlow.Confirm(c,"wait",now,actor);
        var media=Guid.NewGuid();WaterAssayFlow.Confirm(c,"capture",now,actor,media);
        Assert.Throws<AppException>(()=>WaterAssayFlow.ResultSaved(c,Guid.NewGuid(),now,actor));
        WaterAssayFlow.ResultSaved(c,media,now,actor);
        Assert.Equal("cleanup_required",c.State);
        Assert.Equal("waiting_samples",WaterAssayFlow.BatchStatus([c]));
        WaterAssayFlow.Confirm(c,"cleanup",now,actor);
        Assert.Equal("completed",WaterAssayFlow.BatchStatus([c]));
    }
    [Fact] public void FailedChannelDoesNotCompleteOtherChannels()
    {
        var now=DateTime.UtcNow;var a=Guid.NewGuid();var c=Channel("NO3",now);var other=Channel("NH3",now);
        WaterAssayFlow.Fail(c,"rinse","No sample",now,a);
        Assert.Equal("cleanup",WaterAssayFlow.Current(c));
        WaterAssayFlow.Confirm(c,"cleanup",now,a);
        Assert.Equal("failed",c.State);
        Assert.Equal("waiting_samples",WaterAssayFlow.BatchStatus([c,other]));
        Assert.Equal("waiting",other.State);
    }
}
