using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CrabSenseBE.UnitTests.Infrastructure;
public sealed class SensorDiagnosticsTests
{
    [Fact]
    public async Task Test_requires_authentic_current_device_and_fresh_result_without_production_measurements()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        const string secret="test-device-key";
        var device=new Device{DeviceCode="controller",CredentialHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))};
        var sensor=new Sensor{SensorCode="ph",SensorType="pH",Unit="pH",DeviceId=device.Id};
        db.Devices.Add(device);db.Sensors.Add(sensor);await db.SaveChangesAsync();
        var admin=new SensorDiagnosticsController(db){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{
            User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,Guid.NewGuid().ToString())},"test"))}}};
        Assert.IsType<AcceptedResult>(await admin.RequestTest(sensor.Id,default));
        var run=await db.SensorDiagnosticRuns.SingleAsync();
        Assert.Equal("Pending",run.Status);
        await admin.RequestTest(sensor.Id,default);
        Assert.Equal(1,await db.SensorDiagnosticRuns.CountAsync());
        var http=new DefaultHttpContext();
        http.Request.Headers["X-Device-Key"]=secret;http.Request.Headers["X-Edge-Protocol"]="2";
        var edge=new SensorDiagnosticEdgeController(db,new ConfigurationBuilder().Build(),new UnitOfWork(db)){ControllerContext=new ControllerContext{HttpContext=http}};
        Assert.IsType<OkObjectResult>(await edge.Pending(device.DeviceCode,default));
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>edge.Result(run.Id,new SensorTestResult(device.DeviceCode,true,DateTimeOffset.UtcNow.AddHours(-1),7,"pH"),default))).StatusCode);
        await edge.Result(run.Id,new SensorTestResult(device.DeviceCode,true,DateTimeOffset.UtcNow,7,"pH"),default);
        Assert.Equal("Succeeded",run.Status);Assert.Empty(await db.WaterMeasurements.ToListAsync());
        Assert.Equal(409,(await Assert.ThrowsAsync<AppException>(()=>edge.Result(run.Id,new SensorTestResult(device.DeviceCode,true,DateTimeOffset.UtcNow,7,"pH"),default))).StatusCode);
        await admin.RequestTest(sensor.Id,default);
        var expired=await db.SensorDiagnosticRuns.SingleAsync(x=>x.Status=="Pending");expired.ExpiresAt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();
        await admin.Tests(sensor.Id,default);Assert.Equal("Expired",expired.Status);
    }
}
