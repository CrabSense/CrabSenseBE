using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Services;
using Xunit;
namespace CrabSenseBE.UnitTests;
public class DeviceAvailabilityTests
{
    [Fact]
    public void GapsAreNeverCountedAsOnline(){
        var t=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
        var events=new[]{
            new DeviceConnectionEvent{OccurredAt=t,NewStatus="Online"},
            new DeviceConnectionEvent{OccurredAt=t.AddMinutes(5),NewStatus="Offline"},
            new DeviceConnectionEvent{OccurredAt=t.AddMinutes(20),NewStatus="Online",ReasonCode="observation_gap",PreviousObservedAt=t.AddMinutes(7)}
        };
        var result=DeviceAvailability.Calculate(events,new DeviceMonitoringState{ObservedAt=t.AddMinutes(22)},t,t.AddMinutes(30));
        Assert.Equal(420L,result.OnlineSeconds);
        Assert.Equal(120L,result.OfflineSeconds);
        Assert.Equal(1260L,result.UnknownSeconds);
        Assert.Equal(30d,result.CoveragePercent);
    }
    [Fact]
    public void NoSamplesMeansUnknown(){
        var t=DateTime.UtcNow;
        var result=DeviceAvailability.Calculate([],null,t,t.AddMinutes(1));
        Assert.Null(result.AvailabilityPercent);
        Assert.Equal(60L,result.UnknownSeconds);
    }
}