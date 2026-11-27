using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchAmmoniaPolicyTests
{
    [Fact]
    public void RequiresAlignedQualifiedSalinityAndSeparateMeasurands()
    {
        var at=DateTimeOffset.UtcNow.AddMinutes(-1);
        var sample=new Nh3EvidenceEvent(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"0123456789abcdef","abcdef0123456789",
            "1111222233334444","sample",at,"000000000001","000000000002",
            new Nh3SampleReading(100,101,7.5m,102,28m,3000),ClockEstimate:new(at,at.AddMilliseconds(1),at.AddMilliseconds(2),100));
        var m=new WaterMeasurement{Value=15,Unit="ppt",DataQuality="Realtime",EdgeEventKey=new string('a',64),
            MeasuredAt=at.UtcDateTime,ReceivedAt=at.UtcDateTime.AddSeconds(1)};
        var env=WaterBenchAmmoniaPolicy.Environment(sample,m,DateTimeOffset.UtcNow);
        WaterBenchAmmoniaPolicy.Method("printed_table","mg/L NH4",env);
        WaterBenchAmmoniaPolicy.Method("equilibrium","mg/L TAN-N",env);
        Assert.Throws<AppException>(()=>WaterBenchAmmoniaPolicy.Method("printed_table","mg/L TAN-N",env));
        Assert.Throws<AppException>(()=>WaterBenchAmmoniaPolicy.Method("equilibrium","mg/L NH3",env));
        Assert.Throws<AppException>(()=>WaterBenchAmmoniaPolicy.Method("equilibrium","mg/L NH4",env with{SalinityPpt=0}));
        m.MeasuredAt=at.UtcDateTime.AddSeconds(-11);m.ReceivedAt=m.MeasuredAt.AddSeconds(1);
        Assert.Throws<AppException>(()=>WaterBenchAmmoniaPolicy.Environment(sample,m,DateTimeOffset.UtcNow));
    }
}
