using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Api.Controllers;
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

public sealed class ControllerPortInventoryTests
{
    private const string Key = "device-key-test";
    private static ControllerPortsController.PortReport Port(int n) =>
        new($"ssr-{n}", n.ToString(), n + 10, "ssr", "output", "declared", null, true, true);

    private static async Task<(AppDbContext Db, ControllerPortsController Api, Device Device)> SetupAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var device = new Device
        {
            DeviceCode = "esp-test", EdgeState = "Online", Status = DeviceStatus.Online,
            CredentialHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key)))
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        var api = new ControllerPortsController(db, new UnitOfWork(db),
            new ConfigurationBuilder().Build(), Mock.Of<IIotService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        api.Request.Headers["X-Device-Key"] = Key;
        return (db, api, device);
    }

    [Fact]
    public async Task Reports_AddFifthPort_IdempotentReplay_AndRetainRemovedPort()
    {
        var (db, api, device) = await SetupAsync();
        await using var context = db;
        var first = Enumerable.Range(1, 4).Select(Port).ToArray();
        Assert.IsType<OkObjectResult>(await api.Report(device.DeviceCode, new(1, 1, first), default));
        var second = Enumerable.Range(1, 5).Select(Port).ToArray();
        Assert.IsType<OkObjectResult>(await api.Report(device.DeviceCode, new(1, 2, second), default));
        Assert.IsType<OkObjectResult>(await api.Report(device.DeviceCode, new(1, 2, second.Reverse().ToArray()), default));
        Assert.Equal(5, await db.ControllerPorts.CountAsync());
        Assert.IsType<ConflictObjectResult>(await api.Report(device.DeviceCode, new(1, 1, first), default));
        Assert.IsType<ConflictObjectResult>(await api.Report(device.DeviceCode, new(1, 2, first), default));
        Assert.IsType<OkObjectResult>(await api.Report(device.DeviceCode, new(1, 3, first), default));
        var removed = await db.ControllerPorts.SingleAsync(p => p.PortId == "ssr-5");
        Assert.False(removed.Present);
        Assert.Equal(5, await db.ControllerPorts.CountAsync());
    }

    [Fact]
    public async Task Observations_AreStoredOnlyForCurrentInventory()
    {
        var (db, api, device) = await SetupAsync();
        await using var context = db;
        Assert.IsType<OkObjectResult>(await api.Report(device.DeviceCode, new(1, 1, new[] { Port(1), Port(2) }), default));
        var states = new[] { new ControllerPortsController.RelayObservation("1", false), new ControllerPortsController.RelayObservation("2", true) };
        Assert.IsType<OkObjectResult>(await api.Observe(device.DeviceCode, new(1, states), default));
        Assert.True((await db.ControllerPorts.SingleAsync(x => x.Channel == "2")).ObservedOn);
        Assert.IsType<ConflictObjectResult>(await api.Observe(device.DeviceCode, new(2, states), default));
        Assert.IsType<BadRequestObjectResult>(await api.Observe(device.DeviceCode, new(1, states.Take(1).ToArray()), default));
    }

    [Fact]
    public async Task InvalidManifest_OrWrongDeviceCredential_DoesNotWrite()
    {
        var (db, api, device) = await SetupAsync();
        await using var context = db;
        Assert.IsType<BadRequestObjectResult>(await api.Report(device.DeviceCode, new(1, 1, new[] { Port(1), Port(1) }), default));
        api.Request.Headers["X-Device-Key"] = "wrong";
        Assert.IsType<UnauthorizedObjectResult>(await api.Report(device.DeviceCode, new(1, 1, new[] { Port(1) }), default));
        Assert.Empty(await db.ControllerPorts.ToListAsync());
        Assert.Empty(await db.ControllerPortInventories.ToListAsync());
    }
}
