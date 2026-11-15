using Microsoft.EntityFrameworkCore;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Services;
using Xunit;
namespace CrabSenseBE.UnitTests.Application;
public sealed class DeviceMonitoringTests
{
    [Fact]
    public async Task CameraWithOfflineAgentIsNotReportedHealthy()
    {
        var now=DateTime.UtcNow;
        await using var db=new CrabSenseBE.Infrastructure.Persistence.AppDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<CrabSenseBE.Infrastructure.Persistence.AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var camera=new Device{DeviceCode="CAM-HEALTH",DeviceType="camera",Status=DeviceStatus.Online,EdgeState="Approved",LastSeenAt=now};
        var kiosk=new FarmKiosk{Code="KIOSK-HEALTH",Status="Online",LastSeenAt=now};
        db.Devices.Add(camera);db.FarmKiosks.Add(kiosk);
        db.CameraRecordingAgents.Add(new CameraRecordingAgent{DeviceId=camera.Id,KioskId=kiosk.Id,Enabled=true,LastSeenAt=now.AddMinutes(-5)});
        await db.SaveChangesAsync();
        var rows=await new DeviceMonitoringReader(db).ReadAsync(now,CancellationToken.None,false);
        var result=Assert.Single(rows.Where(x=>x.Id==camera.Id));
        Assert.Equal("Online",result.ConnectionStatus);
        Assert.Equal("Offline",result.AgentStatus);
        Assert.Equal("Warning",result.HealthStatus);
    }
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
