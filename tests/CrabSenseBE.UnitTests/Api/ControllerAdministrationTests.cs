using System.Text.Json;
using CrabSenseBE.Api.Controllers;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace CrabSenseBE.UnitTests.Api;

public sealed class ControllerAdministrationTests
{
    private static AppDbContext Database() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IConfiguration Config(bool locked = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["EdgeSync:ControlDisabled"] = locked.ToString() }).Build();

    private static AppUser User(UserRole role) => new()
    {
        Id = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"),
        Email = $"{Guid.NewGuid():N}@example.test", Role = role, IsActive = true
    };

    private static ICurrentUserService Current(AppUser user)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(user.Id);
        return current.Object;
    }

    [Fact]
    public async Task History_IsScopedToController_PaginatesAndFilters_WithoutWritingCommands()
    {
        await using var db = Database();
        var admin = User(UserRole.SystemAdministrator);
        var device = new Device { DeviceCode = "controller-a" };
        var other = new Device { DeviceCode = "controller-b" };
        db.Set<AppUser>().Add(admin);
        db.Devices.AddRange(device, other);
        var now = DateTime.UtcNow;
        db.EdgeCommands.AddRange(
            new EdgeCommand { DeviceCode = device.DeviceCode, Command = "on", Channel = "1", Status = "failed", CreatedAt = now.AddMinutes(-1), CommandSequence = 1 },
            new EdgeCommand { DeviceCode = device.DeviceCode, Command = "off", Channel = "1", Status = "acknowledged", CreatedAt = now, CommandSequence = 2 },
            new EdgeCommand { DeviceCode = other.DeviceCode, Command = "on", Channel = "1", Status = "failed", CreatedAt = now, CommandSequence = 3 });
        await db.SaveChangesAsync();
        var controller = new EdgeCommandsController(Mock.Of<IEdgeCommandService>(), Config(), new UnitOfWork(db), Current(admin));

        var response = Assert.IsType<OkObjectResult>(await controller.History(device.Id, 1, 1));
        var body = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("data");
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());
        Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("off", body.GetProperty("items")[0].GetProperty("command").GetString());

        response = Assert.IsType<OkObjectResult>(await controller.History(device.Id, 1, 25, "failed"));
        body = JsonSerializer.SerializeToElement(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("data");
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(device.DeviceCode, body.GetProperty("items")[0].GetProperty("deviceCode").GetString());
        Assert.IsType<BadRequestObjectResult>(await controller.History(device.Id, 0, 25));
        Assert.IsType<BadRequestObjectResult>(await controller.History(device.Id, 1, 101));
        Assert.IsType<NotFoundObjectResult>(await controller.History(Guid.NewGuid()));
        Assert.Equal(3, await db.EdgeCommands.CountAsync());
    }

    [Fact]
    public async Task History_RejectsNonPlatformDatabaseRole()
    {
        await using var db = Database();
        var staff = User(UserRole.FarmStaff);
        db.Set<AppUser>().Add(staff);
        await db.SaveChangesAsync();
        var controller = new EdgeCommandsController(Mock.Of<IEdgeCommandService>(), Config(), new UnitOfWork(db), Current(staff));
        Assert.IsType<ForbidResult>(await controller.History(Guid.NewGuid()));
    }

    [Fact]
    public async Task Commissioning_RequiresCurrentOffBindingAndPhysicalConfirmation_AndAudits()
    {
        await using var db = Database();
        var admin = User(UserRole.SystemAdministrator);
        var areaId = Guid.NewGuid();
        var device = new Device { DeviceCode = "controller", FarmingAreaId = areaId, CapabilityVersion = 2,
            CredentialHash = "device-specific-hash", EdgeState = "Online", Status = DeviceStatus.Online };
        var port = new ControllerPort { DeviceId = device.Id, PortCode = "SSR_5", PortType = "DIGITAL_OUTPUT",
            Channel = 5, Gpio = 32, ConfigVersion = 2, IsPresent = true, ReportedOn = false,
            CommissioningStatus = "Pending", LastReportedAt = DateTime.UtcNow };
        var system = new WaterSystem { FarmingAreaId = areaId, Name = "RAS" };
        var node = new RasComponent { WaterSystemId = system.Id, Name = "Pump", RelayDeviceId = device.Id,
            ControllerPortId = port.Id, RelayChannel = "5", HasRelay = true };
        db.Set<AppUser>().Add(admin); db.Devices.Add(device); db.ControllerPorts.Add(port);
        db.WaterSystems.Add(system); db.RasComponents.Add(node);
        await db.SaveChangesAsync();
        var api = new ControllerPortsController(new UnitOfWork(db), Config(), Current(admin));
        var request = new ControllerPortsController.CommissioningRequest(2, "Pending", "Verified", true, "Physical inspection complete");
        await Assert.ThrowsAsync<AppException>(() => api.Commission(device.Id, port.Id, request with { PhysicalCheckConfirmed = false }, default));
        await Assert.ThrowsAsync<AppException>(() => api.Commission(device.Id, port.Id, request with { ExpectedConfigVersion = 1 }, default));
        var unlocked = new ControllerPortsController(new UnitOfWork(db), Config(false), Current(admin));
        await Assert.ThrowsAsync<AppException>(() => unlocked.Commission(device.Id, port.Id, request, default));
        port.ReportedOn = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AppException>(() => api.Commission(device.Id, port.Id, request, default));
        port.ReportedOn = false; port.LastReportedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        var command = new EdgeCommand { DeviceCode = device.DeviceCode, Command = "on", Channel = "5",
            Status = "pending", ExpiresAt = DateTime.UtcNow.AddMinutes(1), CommandSequence = 1 };
        db.EdgeCommands.Add(command); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AppException>(() => api.Commission(device.Id, port.Id, request, default));
        command.Status = "failed"; await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await api.Commission(device.Id, port.Id, request, default));
        Assert.Equal("Verified", port.CommissioningStatus);
        Assert.False(port.ReportedOn);
        var log = await db.OperationLogs.SingleAsync(x => x.Action == "ControllerPortCommissioning");
        Assert.Equal(admin.Id, log.UserId);
        Assert.Equal(device.Id, log.EntityId);
        Assert.Single(await db.EdgeCommands.ToListAsync());
        await Assert.ThrowsAsync<AppException>(() => api.Commission(device.Id, port.Id, request, default));
    }
}
