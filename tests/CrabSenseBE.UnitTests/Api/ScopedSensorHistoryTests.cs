using System.Reflection;
using System.Text.Json;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrabSenseBE.UnitTests.Api;

public sealed class ScopedSensorHistoryTests
{
    [Fact]
    public void RouteIsFarmOnlyAndReadOnly()
    {
        Assert.Equal(AppRoles.FarmPortal,typeof(ScopedSensorHistoryController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        var action=typeof(ScopedSensorHistoryController).GetMethod(nameof(ScopedSensorHistoryController.Get))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task HistoryExcludesLegacyOtherAreaOtherRowAndUnknownQualityPreservesRealZero()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var area=Guid.NewGuid();var other=Guid.NewGuid();var row=Guid.NewGuid();
        var actor=new AppUser{Username="history-staff",Role=UserRole.FarmStaff,IsActive=true};
        var sensor=new Sensor{SensorCode="TEST-SAL",SensorType="Salinity",Unit="ppt",FarmingAreaId=area,FarmingRowId=row};
        db.AppUsers.Add(actor);
        db.FarmStaffAssignments.Add(new FarmStaffAssignment{StaffUserId=actor.Id,FarmingAreaId=area});
        db.FarmingRows.Add(new FarmingRow{Id=row,FarmingAreaId=area,Code="TEST"});
        db.Sensors.Add(sensor);
        var now=DateTimeOffset.UtcNow;
        var at=now.AddMinutes(-10).UtcDateTime;
        WaterMeasurement Reading(decimal value,Guid? a,Guid? r,string quality="Realtime",string unit="ppt")=>new(){
            SensorId=sensor.Id,Value=value,ObservedAreaId=a,ObservedRowId=r,ObservedSensorType="Salinity",
            Unit=unit,DataQuality=quality,MeasuredAt=at,ReceivedAt=at.AddSeconds(1),Source="edge:PRIVATE_EVENT"};
        var first=Reading(0,area,row);
        var second=Reading(12,area,row);second.MeasuredAt=at.AddMinutes(-1);
        db.WaterMeasurements.AddRange(first,second,Reading(91,null,null),Reading(92,other,row),Reading(93,area,Guid.NewGuid()),
            Reading(94,area,row,"Unknown"),Reading(95,area,row,"Realtime","ppm"));
        await db.SaveChangesAsync();
        var current=new Mock<ICurrentUserService>();
        current.SetupGet(x=>x.IsAuthenticated).Returns(true);current.SetupGet(x=>x.UserId).Returns(actor.Id);
        var uow=new UnitOfWork(db);
        var c=new ScopedSensorHistoryController(db,uow,current.Object){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        async Task<JsonElement> Page(int p)
        {
            var result=Assert.IsType<OkObjectResult>(await c.Get(area,sensor.Id,now.AddHours(-1),now.AddMinutes(-1),
                farmingRowId:row,asOf:now,page:p,pageSize:1));
            var text=JsonSerializer.Serialize(result.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.DoesNotContain("PRIVATE_EVENT",text);
            return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
        }
        var a=await Page(1);var b=await Page(2);var empty=await Page(3);
        Assert.Equal(0m,Assert.Single(a.GetProperty("items").EnumerateArray().ToArray()).GetProperty("value").GetDecimal());
        Assert.Equal(12m,Assert.Single(b.GetProperty("items").EnumerateArray().ToArray()).GetProperty("value").GetDecimal());
        Assert.True(a.GetProperty("hasMore").GetBoolean());Assert.False(b.GetProperty("hasMore").GetBoolean());
        Assert.Empty(empty.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("no-store",c.Response.Headers["Cache-Control"].ToString());
        Assert.Equal(403,(await Assert.ThrowsAsync<AppException>(()=>c.Get(other,sensor.Id,now.AddHours(-1),now))).StatusCode);
        Assert.Equal(404,(await Assert.ThrowsAsync<AppException>(()=>c.Get(area,sensor.Id,now.AddHours(-1),now,Guid.NewGuid()))).StatusCode);
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>c.Get(area,sensor.Id,now.AddHours(-1),now,page:2))).StatusCode);
        // Simulated reassignment: old readings do not become history of a new row.
        sensor.FarmingRowId=null;await db.SaveChangesAsync();
        var moved=Assert.IsType<OkObjectResult>(await c.Get(area,sensor.Id,now.AddHours(-1),now));
        using var movedJson=JsonDocument.Parse(JsonSerializer.Serialize(moved.Value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Empty(movedJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray());
        actor.Role=UserRole.SystemAdministrator;await db.SaveChangesAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<AppException>(()=>c.Get(area,sensor.Id,now.AddHours(-1),now))).StatusCode);
    }

    [Fact]
    public async Task ConflictingConfiguredAreaCannotBeStamped()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var uow=new UnitOfWork(db);
        var device=new Device{FarmingAreaId=Guid.NewGuid()};
        var sensor=new Sensor{DeviceId=device.Id,FarmingAreaId=Guid.NewGuid()};
        Assert.Null(await SensorHistoryScope.ResolveAsync(uow,sensor,device,default));
        device.FarmingAreaId=sensor.FarmingAreaId;
        Assert.Equal(sensor.FarmingAreaId,(await SensorHistoryScope.ResolveAsync(uow,sensor,device,default))!.AreaId);
    }
}
