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

    [Fact]
    public void SensorStatusRejectsUnverifiedParentAndWrongPortAndReadError()
    {
        var now=DateTime.UtcNow;
        var d=new Device{EdgeState="Pending",Status=DeviceStatus.Online,LastSeenAt=now,CapabilityVersion=2};
        var sensor=new Sensor{DeviceId=d.Id,IsActive=true,LastSeenAt=now};
        Assert.Equal("Unknown",CrabSenseBE.Application.Services.SensorStatusPolicy.Status(sensor,d,null,false,now));
        d.EdgeState="Approved";
        Assert.Equal("Error",CrabSenseBE.Application.Services.SensorStatusPolicy.Status(sensor,d,null,true,now,"Online"));
        sensor.ControllerPortId=Guid.NewGuid();
        var port=new ControllerPort{Id=sensor.ControllerPortId.Value,DeviceId=Guid.NewGuid(),IsPresent=true,ConfigVersion=2};
        Assert.Equal("Offline",CrabSenseBE.Application.Services.SensorStatusPolicy.Status(sensor,d,port,false,now,"Online"));
    }
    [Fact]
    public void FreshHeartbeatIsIndependentOfStoredDisconnectWorkerStatus()
    {
        var now=DateTime.UtcNow;
        var d=new Device{Status=DeviceStatus.Offline,EdgeState="Approved",LastSeenAt=now.AddSeconds(-10)};
        Assert.Equal("Online",DeviceMonitoringReader.DeviceConnection(d,now,30));
        d.EdgeState="Revoked";
        Assert.Equal("Disabled",DeviceMonitoringReader.DeviceConnection(d,now,30));
    }

    private static DeviceMonitoringController AdminController(AppDbContext db,AppUser actor)=>new(db)
    {
        ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext
        {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,actor.Id.ToString())},"test"))}}
    };
    [Fact]
    public async Task ResetPolicyPreservesVersionAndInheritsDefault()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor=new AppUser{Username="admin",Role=UserRole.SystemAdministrator,IsActive=true};
        var d=new Device{DeviceCode="CT",DeviceType="esp32",Status=DeviceStatus.Online,LastSeenAt=DateTime.UtcNow};
        db.AppUsers.Add(actor);db.Devices.Add(d);
        db.DeviceMonitoringPolicies.AddRange(new DeviceMonitoringPolicy{SourceType="controller",SourceId=Guid.Empty,TimeoutSeconds=120,Version=1,UpdatedBy=actor.Id},new DeviceMonitoringPolicy{SourceType="controller",SourceId=d.Id,TimeoutSeconds=30,Version=2,UpdatedBy=actor.Id});
        await db.SaveChangesAsync();
        var api=AdminController(db,actor);
        await api.ResetPolicy("controller",d.Id,2,default);
        var row=await db.DeviceMonitoringPolicies.SingleAsync(x=>x.SourceId==d.Id);
        Assert.True(row.IsInherited);Assert.Equal(3,row.Version);
        Assert.Equal(120,Assert.Single(await new DeviceMonitoringReader(db).ReadAsync(DateTime.UtcNow,default,false)).TimeoutSeconds);
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>api.UpdatePolicy("controller",d.Id,new(45,2),default))).StatusCode);
        await api.UpdatePolicy("controller",d.Id,new(45,3),default);
        Assert.False(row.IsInherited);Assert.Equal(4,row.Version);
    }
    [Fact]
    public async Task MaintenancePlanRejectsOverlapAndDoesNotChangeControlStatus()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor=new AppUser{Username="admin",Role=UserRole.SystemAdministrator,IsActive=true};
        var d=new Device{DeviceCode="CT",DeviceType="esp32",Status=DeviceStatus.Online,LastSeenAt=DateTime.UtcNow};
        db.AppUsers.Add(actor);db.Devices.Add(d);await db.SaveChangesAsync();
        var api=AdminController(db,actor);var now=DateTimeOffset.UtcNow;
        await api.CreateMaintenance("controller",d.Id,new(now.AddMinutes(10),now.AddHours(1),"Inspect wiring"),default);
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>api.CreateMaintenance("controller",d.Id,new(now.AddMinutes(20),now.AddHours(2),"Overlap"),default))).StatusCode);
        var plan=await db.DeviceMaintenanceWindows.SingleAsync();Assert.Equal(actor.Id,plan.CreatedBy);
        Assert.Equal(DeviceStatus.Online,d.Status);
        await api.CancelMaintenance(plan.Id,default);Assert.NotNull(plan.CancelledAt);Assert.Equal<Guid?>(actor.Id,plan.CancelledBy);
        Assert.Equal(DeviceStatus.Online,d.Status);
    }
    [Fact]
    public async Task IncidentCannotCloseBeforeRecoveryAndRecordsProcessingActor()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor=new AppUser{Username="admin",Role=UserRole.SystemAdministrator,IsActive=true};
        var a=new Alert{SourceType="Device",IncidentType="device_disconnect",Status=AlertStatus.Active};
        db.AppUsers.Add(actor);db.Alerts.Add(a);await db.SaveChangesAsync();
        var api=AdminController(db,actor);
        await api.Acknowledge(a.Id,default);await api.ProcessAlert(a.Id,new("Investigating"),default);
        Assert.Equal(AlertStatus.InProgress,a.Status);Assert.Equal<Guid?>(actor.Id,a.ProcessingBy);
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>api.CloseAlert(a.Id,new("Still offline"),default))).StatusCode);
        a.Status=AlertStatus.Recovered;a.ResolvedAt=DateTime.UtcNow;await db.SaveChangesAsync();
        await api.CloseAlert(a.Id,new("Verified recovery"),default);
        Assert.Equal(AlertStatus.Resolved,a.Status);Assert.Equal("Verified recovery",a.ResolutionNote);
    }
}
