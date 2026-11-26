using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchNh3PolicyTests
{
    private static Nh3EvidenceEvent Sample()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"0123456789abcdef","abcdef0123456789",
        "1111222233334444","sample",DateTimeOffset.UtcNow,"000000000001","000000000002",
        new Nh3SampleReading(uint.MaxValue-100,50,7.5m,100,28.5m,3000));
    [Fact]
    public void WrapAwareSampleAndMissingValuesAreAccepted()
    {
        var e=Sample();WaterBenchNh3Policy.Validate(e);
        WaterBenchNh3Policy.Validate(e with{Sample=e.Sample! with{Ph=null,TemperatureC=null,PhObservedUptimeMs=5000}});
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Validate(e with{Sample=e.Sample! with{Ph=15}}));
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Validate(e with{Sample=e.Sample! with{TemperatureC=61}}));
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Validate(e with{Sample=e.Sample! with{TemperatureObservedUptimeMs=5000}}));
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Validate(e with{Sample=e.Sample! with{TemperatureRegisterMaxAgeMs=4000}}));
    }
    [Fact]
    public void ClockIsOnlyBoundedEstimate()
    {
        var e=Sample();var time=e.ReceivedAt.AddSeconds(-1);
        var c=new Nh3ClockEstimate(time,time.AddMilliseconds(151),time.AddMilliseconds(201),200);
        WaterBenchNh3Policy.Validate(e with{ClockEstimate=c});
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Validate(e with{ClockEstimate=c with{EstimatedPhObservationUtc=time.AddSeconds(10)}}));
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Validate(e with{ClockEstimate=c with{AnchorRoundTripMs=4000}}));
    }
    [Fact]
    public void MatchSessionAndActualRequestedCapture()
    {
        var e=Sample();
        var start=new BenchBatchEvent(e.StationId,Guid.NewGuid(),e.ConnectionId,e.BootId,e.BatchId,1,"start",e.ReceivedAt.AddSeconds(-1),Mode:"manual",Mask:4);
        var s=start with{EventId=Guid.NewGuid(),Type="session",Sequence=2,Analyte="NH3",SessionId=e.SessionId};
        var t=new WaterBenchBatchTimeline();t.Apply(start);t.Apply(s);
        WaterBenchNh3Policy.Match(e,t,s,null);
        var r=s with{EventId=Guid.NewGuid(),Type="reaction",Sequence=3,ReactionStartMs=1000,ReactionDurationMs=600000};
        t.Apply(r);
        var i=s with{EventId=Guid.NewGuid(),Type="image",Sequence=4,CaptureId=7,ReceivedAt=e.ReceivedAt.AddSeconds(601)};
        // Keep test timestamps within allowed wall-clock range.
        i=i with{ReceivedAt=e.ReceivedAt};
        t.Apply(i);
        var link=e with{EventId=Guid.NewGuid(),Type="image_link",Sample=null,Image=new Nh3ImageObservation(7,601500,601000)};
        WaterBenchNh3Policy.Validate(link);WaterBenchNh3Policy.Match(link,t,s,i);
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Match(link with{Image=link.Image! with{CaptureId=8}},t,s,i));
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Match(link with{Image=link.Image! with{ReactionFinishedUptimeMs=600999}},t,s,i));
        Assert.Throws<AppException>(()=>WaterBenchNh3Policy.Match(e with{ConnectionId=Guid.NewGuid()},t,s,i));
    }
}
