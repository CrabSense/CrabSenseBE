using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Services;
using Xunit;
namespace CrabSenseBE.UnitTests.Application;
public sealed class DeviceMonitoringTests
{
    [Fact] public void FreshnessDoesNotTreatUnseenAsOnline(){
        var now=DateTime.UtcNow;
        Assert.Equal("Unknown",DeviceMonitoringReader.Fresh(null,now,120));
        Assert.Equal("Offline",DeviceMonitoringReader.Fresh(now.AddSeconds(-121),now,120));
        Assert.Equal("Online",DeviceMonitoringReader.Fresh(now.AddSeconds(-119),now,120));
        Assert.Equal("Unknown",DeviceMonitoringReader.DeviceConnection(new Device{EdgeState="Unprovisioned",Status=DeviceStatus.Online,LastSeenAt=now},now));
        Assert.Equal("Offline",DeviceMonitoringReader.DeviceConnection(new Device{EdgeState="Online",Status=DeviceStatus.Online,LastSeenAt=now.AddMinutes(-16)},now));
        Assert.Equal("Disabled",DeviceMonitoringReader.KioskConnection(new FarmKiosk{Status="Revoked",LastSeenAt=now},now));
    }
}
