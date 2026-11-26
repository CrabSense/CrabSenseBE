using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchBatchTimelineTests
{
    private static BenchBatchEvent Start()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"0123456789abcdef","abcdef0123456789",1,"start",
        DateTimeOffset.UtcNow,Mode:"manual",Mask:1);
    private static BenchBatchEvent Next(BenchBatchEvent e,int seq,string type)=>e with{EventId=Guid.NewGuid(),Sequence=seq,Type=type};
    [Fact]
    public void ObservationsTrackReactionAndImageWithoutValidatedResult()
    {
        var e=Start();var t=new WaterBenchBatchTimeline();t.Apply(e);
        var s=Next(e,2,"session") with{Analyte="NO2",SessionId=e.BatchId+"-1"};t.Apply(s);
        t.Apply(Next(s,3,"reaction") with{ReactionStartMs=uint.MaxValue-10,ReactionDurationMs=300000});
        t.Apply(Next(s,4,"image") with{CaptureId=65536});
        t.Apply(Next(e,5,"status") with{State=2,Started=1,Reacting=0,Imaged=1,Drained=1});
        Assert.Equal("firmware_complete_reported",t.Status);
        Assert.Equal("drained_reported",Assert.Single(t.Channels).Status);
        Assert.False(t.ChemicalResultValidated);Assert.False(t.PhysicalCompletionVerified);Assert.False(t.CleaningVerified);
        Assert.Throws<AppException>(()=>t.Apply(Next(e,6,"session")));
    }
    [Fact]
    public void RejectsGapWrongConnectionWrongSessionAndMissingEvidence()
    {
        var e=Start();var t=new WaterBenchBatchTimeline();t.Apply(e);
        Assert.Throws<AppException>(()=>t.Apply(Next(e,3,"session")));
        Assert.Throws<AppException>(()=>t.Apply(Next(e,2,"session") with{ConnectionId=Guid.NewGuid()}));
        Assert.Throws<AppException>(()=>t.Apply(Next(e,2,"session") with{Analyte="NO2",SessionId=e.BatchId+"-2"}));
        Assert.Throws<AppException>(()=>t.Apply(Next(e,2,"status") with{State=2,Started=1,Reacting=0,Imaged=1,Drained=1}));
        t.Apply(Next(e,2,"interrupted"));
        Assert.Equal("interrupted_observed",t.Status);
    }
    [Fact]
    public void ScheduledRequiresAllThree()
    {
        var t=new WaterBenchBatchTimeline();
        Assert.Throws<AppException>(()=>t.Apply(Start() with{Mode="scheduled"}));
        t.Apply(Start() with{Mode="scheduled",Mask=7});
        Assert.Equal(3,t.Channels.Count);
    }
}
