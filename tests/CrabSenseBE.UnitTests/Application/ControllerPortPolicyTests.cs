using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrabSenseBE.UnitTests.Application;

public sealed class ControllerPortPolicyTests
{
    [Fact]
    public async Task OnlyCurrentOutputCanBeControlled_AndOnRequiresCapability()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var device = new Device { DeviceCode = "port-policy" };
        db.Devices.Add(device);
        db.ControllerPortInventories.Add(new() { DeviceId = device.Id, Revision = 2, ReportedAt = DateTime.UtcNow });
        db.ControllerPorts.Add(new() { DeviceId = device.Id, PortId = "ssr-5", Channel = "5", Kind = "ssr", Direction = "output", Present = true });
        await db.SaveChangesAsync();
        var policy = new ControllerPortPolicy(db);
        Assert.True((await policy.CheckAsync(device.Id, "5", "off", default))!.Allowed);
        Assert.False((await policy.CheckAsync(device.Id, "5", "on", default))!.Allowed);
        Assert.False((await policy.CheckAsync(device.Id, "6", "off", default))!.Allowed);
        Assert.Equal(2L, (await policy.CheckAsync(device.Id, "5", "off", default))!.Revision);
        Assert.Null(await policy.CheckAsync(Guid.NewGuid(), "5", "off", default));
        var p = await db.ControllerPorts.SingleAsync(); p.Present = false;
        await db.SaveChangesAsync();
        Assert.False((await policy.CheckAsync(device.Id, "5", "off", default))!.Allowed);
        Assert.True((await policy.CheckAsync(device.Id, null, "alloff", default))!.Allowed);
    }
}
