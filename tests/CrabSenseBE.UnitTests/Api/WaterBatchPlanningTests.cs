using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using Xunit;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBatchPlanningTests
{
    [Fact] public void ScheduledAlwaysRequiresAllThreeUniqueAnalytes()
    {
        var a=new WaterBatchPlanning.Target("NO2",Guid.NewGuid());
        var b=new WaterBatchPlanning.Target("NO3",Guid.NewGuid());
        var c=new WaterBatchPlanning.Target("NH3",Guid.NewGuid());
        Assert.Single(WaterBatchPlanning.Normalize([a],false));
        Assert.Equal(3,WaterBatchPlanning.Normalize([a,b,c],true).Length);
        Assert.Throws<AppException>(()=>WaterBatchPlanning.Normalize([a,b],true));
        Assert.Throws<AppException>(()=>WaterBatchPlanning.Normalize([a,a,c],true));
        Assert.Equal("Sera",WaterBatchPlanning.Kit(a.Analyte));
        Assert.Equal("VASTOCEAN",WaterBatchPlanning.Kit(b.Analyte));
        Assert.Equal("Thủy Phước",WaterBatchPlanning.Kit(c.Analyte));
    }
    [Fact] public void ScheduleSkipsMissedPeriodsRatherThanBacklogging()
    {
        var due=new DateTime(2026,11,15,0,0,0,DateTimeKind.Utc);
        Assert.Equal(due.AddHours(36),WaterBatchPlanning.Next(due,due.AddHours(25),720));
        var batch=WaterBatchPlanning.New(Guid.NewGuid(),"scheduled",[
            new("NO2",Guid.NewGuid()),new("NO3",Guid.NewGuid()),new("NH3",Guid.NewGuid())],"RAS_RETURN",due);
        Assert.Equal("waiting_samples",batch.Status);
        Assert.Null(batch.CompletedAt);
    }
}
