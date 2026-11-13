using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
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
