using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Infrastructure.Persistence;
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
        var areaId=Guid.NewGuid();
        var camera=new Device{DeviceCode="CAM-HEALTH",DeviceType="camera",FarmingAreaId=areaId,Status=DeviceStatus.Online,EdgeState="Approved",LastSeenAt=now};
        var kiosk=new FarmKiosk{Code="KIOSK-HEALTH",FarmingAreaId=areaId,Status="Online",LastSeenAt=now};
        db.Devices.Add(camera);db.FarmKiosks.Add(kiosk);
        db.CameraRecordingAgents.Add(new CameraRecordingAgent{DeviceId=camera.Id,KioskId=kiosk.Id,Enabled=true,LastSeenAt=now.AddMinutes(-5)});
        var endedAt=now.AddMinutes(-4);
        db.CameraRecordingExecutions.Add(new CameraRecordingExecution{DeviceId=camera.Id,ScheduleId=Guid.NewGuid(),StartedAt=now.AddMinutes(-5),EndedAt=endedAt,LastHeartbeatAt=endedAt,Status="Completed"});
        await db.SaveChangesAsync();
        var rows=await new DeviceMonitoringReader(db).ReadAsync(now,CancellationToken.None,false);
        var result=Assert.Single(rows.Where(x=>x.Id==camera.Id));
        Assert.Equal("Online",result.ConnectionStatus);
        Assert.Equal("Offline",result.AgentStatus);
        Assert.Equal("Warning",result.HealthStatus);
        Assert.Equal<DateTime?>(endedAt,result.LastRecordingAt);
    }
        [Fact] public void FreshnessDoesNotTreatUnseenAsOnline(){
        var now=DateTime.UtcNow;
        Assert.Equal("Unknown",DeviceMonitoringReader.Fresh(null,now,120));
        Assert.Equal("Unknown",DeviceMonitoringReader.Fresh(now.AddSeconds(6),now,120));
        Assert.Equal("Offline",DeviceMonitoringReader.Fresh(now.AddSeconds(-121),now,120));
        Assert.Equal("Online",DeviceMonitoringReader.Fresh(now.AddSeconds(-119),now,120));
        Assert.Equal("Unknown",DeviceMonitoringReader.DeviceConnection(new Device{EdgeState="Unprovisioned",Status=DeviceStatus.Online,LastSeenAt=now},now));
        Assert.Equal("Offline",DeviceMonitoringReader.DeviceConnection(new Device{EdgeState="Online",Status=DeviceStatus.Online,LastSeenAt=now.AddMinutes(-16)},now));
        Assert.Equal("Disabled",DeviceMonitoringReader.KioskConnection(new FarmKiosk{Status="Revoked",LastSeenAt=now},now));
    }

    [Fact]
    public async Task TechnicalAlertAcknowledgementRecordsActorAndRejectsDuplicate()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor=new AppUser{Username="monitoring-admin",Email="admin@example.test",Role=UserRole.SystemAdministrator,IsActive=true};
        var alert=new Alert{SourceType="Device",SourceId=Guid.NewGuid().ToString("N"),IncidentType="device_disconnect",Status=AlertStatus.Active};
        db.AppUsers.Add(actor);db.Alerts.Add(alert);await db.SaveChangesAsync();
        var controller=new DeviceMonitoringController(db)
        {
            ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext
            {
                User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,actor.Id.ToString())},"test"))
            }}
        };
        await controller.Acknowledge(alert.Id,CancellationToken.None);
        Assert.Equal(AlertStatus.Acknowledged,alert.Status);
        Assert.Equal<Guid?>(actor.Id,alert.AcknowledgedBy);
        Assert.NotNull(alert.AcknowledgedAt);
        var conflict=await Assert.ThrowsAsync<AppException>(()=>controller.Acknowledge(alert.Id,CancellationToken.None));
        Assert.Equal(409,conflict.StatusCode);
        Assert.Single(await db.OperationLogs.Where(x=>x.EntityId==alert.Id&&x.Action=="AcknowledgeDeviceMonitoringAlert").ToListAsync());
    }
    [Fact]
    public async Task MeasurementEnrichmentUsesLatestMeasurementAndMostRecentReceipt()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now=DateTime.UtcNow;var id=Guid.NewGuid();
        db.WaterMeasurements.AddRange(
            new WaterMeasurement{SensorId=id,Value=10,MeasuredAt=now.AddMinutes(-3),ReceivedAt=now,DataQuality="Good"},
            new WaterMeasurement{SensorId=id,Value=12,MeasuredAt=now.AddMinutes(-1),ReceivedAt=now.AddSeconds(-30),DataQuality="Good"},
            new WaterMeasurement{SensorId=Guid.NewGuid(),Value=99,MeasuredAt=now,ReceivedAt=now});
        await db.SaveChangesAsync();
        var row=new MonitoringRow(id,"sensor","S-1",null,null,null,null,null,"Online","Healthy",null,now,900);
        var enriched=Assert.Single(await new DeviceMonitoringReader(db).AddMeasurementsAsync([row],CancellationToken.None));
        Assert.Equal<decimal?>(12m,enriched.LatestValue);
        Assert.Equal<DateTime?>(now.AddMinutes(-1),enriched.LatestMeasuredAt);
        Assert.Equal<DateTime?>(now,enriched.LastReceivedAt);
        Assert.Equal("Good",enriched.DataQuality);
    }
}
