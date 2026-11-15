using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace CrabSenseBE.UnitTests.Api;
public sealed class CameraRecordingAgentTests
{
    private static AppDbContext Db()=>new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Fact]
    public async Task Managed_assignment_requires_same_area_and_revocation_blocks_legacy_key()
    {
        await using var db=Db();
        var user=new AppUser{Role=UserRole.SystemAdministrator,IsActive=true};
        var area=Guid.NewGuid();
        var camera=new Device{DeviceCode="CAM-AGENT",DeviceType="camera",FarmingAreaId=area};
        var kiosk=new FarmKiosk{Code="K1",FarmingAreaId=area,Status="Online"};
        var other=new FarmKiosk{Code="K2",FarmingAreaId=Guid.NewGuid(),Status="Online"};
        var key=new string('k',40);var otherKey=new string('o',40);
        db.AppUsers.Add(user);db.Devices.Add(camera);db.FarmKiosks.AddRange(kiosk,other);
        db.KioskCredentials.AddRange(new KioskCredential{KioskId=kiosk.Id,SecretHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))},
            new KioskCredential{KioskId=other.Id,SecretHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(otherKey)))});
        await db.SaveChangesAsync();
        var manage=new CameraRecordingAgentsController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        manage.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id.ToString())},"test"));
        await Assert.ThrowsAsync<AppException>(()=>manage.Assign(camera.Id,new(other.Id,true,0),default));
        await manage.Assign(camera.Id,new(kiosk.Id,true,0),default);
        var runtime=new CameraRecordingRuntimeController(db,new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CameraRecording:AgentKeys:"+camera.Id.ToString("D"),new string('x',40)}}).Build())
            {ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        runtime.Request.Headers["X-Kiosk-Key"]=otherKey;
        Assert.Equal(403,(await Assert.ThrowsAsync<AppException>(()=>runtime.Configuration(camera.Id,default))).StatusCode);
        runtime.Request.Headers["X-Kiosk-Key"]=key;
        Assert.IsType<OkObjectResult>(await runtime.Configuration(camera.Id,default));
        manage.Request.Headers["X-Kiosk-Key"]=key;
        await manage.Heartbeat(camera.Id,new("test",2),default);
        Assert.Equal(2,(await db.CameraRecordingAgents.SingleAsync()).PendingClips);
        await manage.Assign(camera.Id,new(null,false,1),default);
        runtime.Request.Headers["X-Camera-Agent-Key"]=new string('x',40);
        Assert.Equal(403,(await Assert.ThrowsAsync<AppException>(()=>runtime.Configuration(camera.Id,default))).StatusCode);
    }
    [Fact]
    public async Task Monitor_creates_and_resolves_failure_alert_without_claiming_recovery_of_video()
    {
        await using var db=Db();
        var camera=new Device{DeviceCode="CAM-MON",DeviceType="camera"};
        var schedule=new CameraSchedule{Mode="Continuous",Enabled=true};
        var now=DateTime.UtcNow;
        var e=new CameraRecordingExecution{DeviceId=camera.Id,ScheduleId=schedule.Id,Status="Failed",StartedAt=now,EndedAt=now,LastHeartbeatAt=now};
        db.Devices.Add(camera);db.CameraSchedules.Add(schedule);db.CameraScheduleAssignments.Add(new(){DeviceId=camera.Id,ScheduleId=schedule.Id});db.CameraRecordingExecutions.Add(e);
        await db.SaveChangesAsync();
        await CameraRecordingMonitor.Sweep(db,now,default);
        Assert.Equal(AlertStatus.Active,(await db.Alerts.SingleAsync(x=>x.IncidentType=="camera_recording_failure")).Status);
        await CameraRecordingMonitor.Sweep(db,now.AddSeconds(10),default);
        Assert.Single(await db.Alerts.Where(x=>x.IncidentType=="camera_recording_failure").ToListAsync());
        db.CameraRecordingExecutions.Add(new(){DeviceId=camera.Id,ScheduleId=schedule.Id,Status="Recording",StartedAt=now.AddSeconds(15),LastHeartbeatAt=now.AddSeconds(15)});
        await db.SaveChangesAsync();await CameraRecordingMonitor.Sweep(db,now.AddSeconds(20),default);
        Assert.Equal(AlertStatus.Resolved,(await db.Alerts.SingleAsync(x=>x.IncidentType=="camera_recording_failure")).Status);
    }

    [Fact]
    public async Task FrameEvidenceRequiresCurrentAssignmentAndRejectsFutureTimestamp()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var area=Guid.NewGuid();var key="monitoring-kiosk-secret";
        var kiosk=new FarmKiosk{Code="K",FarmingAreaId=area,Status="Online"};
        var camera=new Device{DeviceCode="CAM",DeviceType="camera",FarmingAreaId=area};
        var agent=new CameraRecordingAgent{DeviceId=camera.Id,KioskId=kiosk.Id,Enabled=true,Version=2};
        db.FarmKiosks.Add(kiosk);db.Devices.Add(camera);db.CameraRecordingAgents.Add(agent);
        db.KioskCredentials.Add(new KioskCredential{KioskId=kiosk.Id,SecretHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))});
        await db.SaveChangesAsync();
        var api=new CameraRecordingAgentsController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        api.Request.Headers["X-Kiosk-Key"]=key;
        var now=DateTimeOffset.UtcNow;
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>api.Heartbeat(camera.Id,new("v2",0,1,"Verified",now),default))).StatusCode);
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>api.Heartbeat(camera.Id,new("v2",0,2,"Verified",now.AddMinutes(5)),default))).StatusCode);
        await api.Heartbeat(camera.Id,new("v2",0,2,"Verified",now),default);
        Assert.Equal("Verified",agent.StreamProbeStatus);Assert.NotNull(agent.LastFrameObservedAt);
        await api.Heartbeat(camera.Id,new("v2",0,2,"Failed",now.AddSeconds(-1)),default);
        Assert.Equal("Verified",agent.StreamProbeStatus);
    }
}
