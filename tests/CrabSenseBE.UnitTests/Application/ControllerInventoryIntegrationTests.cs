using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace CrabSenseBE.UnitTests.Application;

public sealed class ControllerInventoryIntegrationTests
{
    private const string Key = "test-device-key";
    private static PortManifestItem Port(int n) => new($"SSR_{n}", "DIGITAL_OUTPUT", n, n + 10, ActiveLevel: "HIGH", On: false);

    [Fact]
    public async Task PreparedProfile_RequiresActiveAreaAndCannotAuthenticateBeforeApproval()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var area=new FarmingArea { Code="TEST-AREA",Name="Test",IsActive=true };
        db.FarmingAreas.Add(area); await db.SaveChangesAsync();
        var uow=new UnitOfWork(db);
        var service=new CrabSenseBE.Application.Services.IotService(uow,Mock.Of<IStorageService>(),Mock.Of<IAlertService>(),Mock.Of<IRasFlowService>(),Microsoft.Extensions.Logging.Abstractions.NullLogger<CrabSenseBE.Application.Services.IotService>.Instance);
        var request=new CrabSenseBE.Application.DTOs.IoT.CreateDeviceRequest("ESP-PREPARED",FarmingAreaId:area.Id,PrepareOnly:true);
        await Assert.ThrowsAsync<AppException>(()=>service.CreateDeviceAsync(request with { FarmingAreaId=null }));
        await Assert.ThrowsAsync<AppException>(()=>service.CreateDeviceAsync(request with { ApiKey="legacy-key" }));
        var result=await service.CreateDeviceAsync(request);
        Assert.True(result.Success);
        var device=await db.Devices.SingleAsync();
        Assert.Equal("Unprovisioned",device.EdgeState);
        Assert.Equal(DeviceStatus.Offline,device.Status);
        Assert.Null(device.CredentialHash);
        Assert.Null(device.KioskId);
        Assert.Equal(area.Id,device.FarmingAreaId);
        var duplicate=await Assert.ThrowsAsync<AppException>(()=>service.CreateDeviceAsync(request with { DeviceCode="esp-prepared" }));
        Assert.Equal(409,duplicate.StatusCode);
        var context=new DefaultHttpContext();context.Request.Headers["X-Edge-Key"]="legacy-key";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"EdgeSync:ApiKey","legacy-key"}}).Build();
        var denied=await CrabSenseBE.Api.Services.EdgeKeyGuard.CheckDeviceAsync(context.Request,config,uow,device.DeviceCode,default);
        Assert.IsType<UnauthorizedObjectResult>(denied.Denied);
        Assert.Null(denied.Identity);
    }

    [Fact]
    public async Task DiscoverFifthPort_PreservesExistingIdentity_AndStoresCurrentObservations()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var device = new Device { DeviceCode = "esp-test", EdgeState = "Online", Status = DeviceStatus.Online,
            CredentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key))) };
        db.Devices.Add(device); await db.SaveChangesAsync();
        var api = new ControllerPortsController(new UnitOfWork(db), new ConfigurationBuilder().Build(), Mock.Of<ICurrentUserService>())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        api.Request.Headers["X-Device-Key"] = Key;
        api.Request.Headers["X-Edge-Protocol"] = "2";
        ControllerManifest Manifest(int rev, int count) => new(device.DeviceCode, "ESP32-V1", "RC2", rev, Enumerable.Range(1, count).Select(Port).ToArray());
        Assert.IsType<OkObjectResult>(await api.Discover(Manifest(1,4), default));
        var first = await db.ControllerPorts.SingleAsync(x => x.Channel == 1);
        first.CommissioningStatus = "Verified"; await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await api.Discover(Manifest(2,5), default));
        Assert.Equal(5, await db.ControllerPorts.CountAsync());
        Assert.Equal("Verified", (await db.ControllerPorts.SingleAsync(x => x.Id == first.Id)).CommissioningStatus);
        Assert.Equal("Pending", (await db.ControllerPorts.SingleAsync(x => x.Channel == 5)).CommissioningStatus);
        Assert.IsType<OkObjectResult>(await api.Discover(Manifest(2,5), default));
        Assert.Equal(2, await db.ControllerEvents.CountAsync(x => x.Kind == "manifest"));
        Assert.InRange(await db.ControllerEvents.CountAsync(x => x.Kind == "heartbeat"), 1, 2);
        Assert.Contains("SSR_5", (await db.ControllerEvents.SingleAsync(x => x.EventKey == "manifest:2")).DetailsJson);
        await Assert.ThrowsAsync<AppException>(() => api.Discover(Manifest(1,4), default));
        var states = Enumerable.Range(1,5).Select(n => new ControllerPortsController.RelayObservation(n, n==5)).ToArray();
        Assert.IsType<OkObjectResult>(await api.Observe(device.DeviceCode, new(2,states), default));
        Assert.True((await db.ControllerPorts.SingleAsync(x => x.Channel == 5)).ReportedOn);
        await Assert.ThrowsAsync<AppException>(() => api.Observe(device.DeviceCode, new(1,states), default));
        Assert.IsType<OkObjectResult>(await api.Discover(Manifest(3,4), default));
        var removed = await db.ControllerPorts.SingleAsync(x => x.Channel == 5);
        Assert.False(removed.IsPresent); Assert.Null(removed.ReportedOn);
    }
}
