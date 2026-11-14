using System.Security.Claims;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CrabSenseBE.UnitTests.Api;
public sealed class CameraScheduleTests
{
    private static CameraSchedulesController.ScheduleRequest Request(Guid id, params CameraSchedulesController.Slot[] slots)
        => new("Night","Scheduled",true,"Asia/Ho_Chi_Minh",30,"1080p",15,100,new[]{id},slots);
    [Fact]
    public void Overnight_wraps_Sunday_to_Monday_and_rejects_overlap()
    {
        var r=Request(Guid.NewGuid(),new CameraSchedulesController.Slot(7,"18:00","06:00"));
        var slots=CameraSchedulesController.Normalize(r);
        Assert.Equal(2,slots.Count);
        Assert.Contains(slots,x=>x.Day==1&&x.StartMinute==0&&x.EndMinute==360);
        Assert.Contains(slots,x=>x.Day==7&&x.StartMinute==1080&&x.EndMinute==1440);
        Assert.Throws<AppException>(()=>CameraSchedulesController.Normalize(r with {Slots=new[]{new CameraSchedulesController.Slot(7,"18:00","06:00"),new CameraSchedulesController.Slot(1,"05:00","07:00")}}));
        var schedule=new CameraSchedule{Enabled=true,Mode="Scheduled",Timezone=r.Timezone};
        Assert.True(CameraSchedulesController.IsDue(schedule,slots,new DateTime(2026,11,15,22,0,0,DateTimeKind.Utc)));
        Assert.False(CameraSchedulesController.IsDue(schedule,slots,new DateTime(2026,11,15,23,0,0,DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026,11,22,11,0,0,DateTimeKind.Utc),CameraSchedulesController.NextDue(schedule,slots,new DateTime(2026,11,15,23,0,0,DateTimeKind.Utc)));
    }
    [Fact]
    public async Task CRUD_projects_legacy_config_and_preserves_media()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var camera=new Device{DeviceCode="CAM-A",DeviceType="camera"};
        var other=new Device{DeviceCode="CAM-B",DeviceType="camera"};
        db.Devices.AddRange(camera,other);await db.SaveChangesAsync();
        var api=new CameraSchedulesController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        api.HttpContext.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString())},"test"));
        var req=Request(camera.Id,new CameraSchedulesController.Slot(1,"06:00","18:00")) with {CameraIds=new[]{camera.Id,other.Id}};
        Assert.IsType<OkObjectResult>(await api.Create(req,default));
        var schedule=await db.CameraSchedules.SingleAsync();
        Assert.Equal(2,await db.CameraScheduleAssignments.CountAsync());
        Assert.Equal(2,await db.CameraRecordingSettings.CountAsync());
        await Assert.ThrowsAsync<AppException>(()=>api.Create(req,default));
        await api.Status(schedule.Id,new(false,1),default);
        Assert.All(await db.CameraRecordingSettings.ToListAsync(),s=>Assert.Equal("Disabled",s.Mode));
        await Assert.ThrowsAsync<AppException>(()=>api.Update(schedule.Id,req with {ExpectedVersion=1},default));
        var media=new MediaAsset{DeviceId=camera.Id,FileName="test.mp4",Category="video"};
        db.MediaAssets.Add(media);await db.SaveChangesAsync();
        await api.Delete(schedule.Id,2,default);
        Assert.True(schedule.Deleted);Assert.Empty(await db.CameraScheduleAssignments.ToListAsync());
        Assert.Single(await db.MediaAssets.ToListAsync());
    }
    [Fact]
    public async Task Runtime_requires_camera_specific_key_and_current_schedule_version()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var camera=new Device{DeviceCode="CAM-A",DeviceType="camera"};
        var schedule=new CameraSchedule{Mode="Continuous",Enabled=true};
        db.Devices.Add(camera);db.CameraSchedules.Add(schedule);
        db.CameraScheduleAssignments.Add(new(){DeviceId=camera.Id,ScheduleId=schedule.Id});await db.SaveChangesAsync();
        var key=new string('a',40);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"CameraRecording:AgentKeys:"+camera.Id.ToString("D"),key}}).Build();
        var api=new CameraRecordingRuntimeController(db,config){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        await Assert.ThrowsAsync<AppException>(()=>api.Configuration(camera.Id,default));
        api.Request.Headers["X-Camera-Agent-Key"]=key;
        var id=Guid.NewGuid();
        await Assert.ThrowsAsync<AppException>(()=>api.Start(camera.Id,new(id,schedule.Id,2),default));
        await api.Start(camera.Id,new(id,schedule.Id,1),default);
        await api.Start(camera.Id,new(id,schedule.Id,1),default);
        Assert.Single(await db.CameraRecordingExecutions.ToListAsync());
        await Assert.ThrowsAsync<AppException>(()=>api.Start(camera.Id,new(Guid.NewGuid(),schedule.Id,1),default));
        await api.Finish(camera.Id,id,new("Failed","test recording failure",null),default);
        Assert.Equal("Failed",(await db.CameraRecordingExecutions.SingleAsync()).Status);
        var failedId=Guid.NewGuid();
        await api.Failure(camera.Id,new(failedId,schedule.Id,1,"FFmpeg failed"),default);
        await api.Failure(camera.Id,new(failedId,schedule.Id,1,"FFmpeg failed"),default);
        Assert.Equal(2,await db.CameraRecordingExecutions.CountAsync());
        Assert.Equal("Failed",(await db.CameraRecordingExecutions.SingleAsync(x=>x.Id==failedId)).Status);
    }
}
