using System.Linq.Expressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class RasAutomaticRelaySafetyTests
{
    [Theory]
    [InlineData("inactive-float", "unknown")]
    [InlineData("stale", "unknown")]
    [InlineData("future", "unknown")]
    [InlineData("conflict", "unsafe")]
    [InlineData("controller-offline", "unsafe")]
    public async Task UnsafeOrUnknownEvidence_DoesNotQueueRelay(
        string scenario,
        string expectedStatus)
    {
        var fixture = CreateFixture(scenario);

        await fixture.Service.ApplyAutoRelaysAsync(fixture.Device.Id);

        fixture.Edge.Verify(service => service.EnqueueAutomationAsync(
            It.IsAny<EnqueueEdgeCommandRequest>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Pump.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task AcknowledgedCommandWithoutRelayState_IsNotRetried()
    {
        var fixture = CreateFixture("valid", new EdgeCommand
        {
            Id = Guid.NewGuid(),
            DeviceCode = "edge-a",
            Channel = "1",
            Command = "on",
            Status = "acknowledged",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTime.UtcNow.AddMinutes(4)
        });

        await fixture.Service.ApplyAutoRelaysAsync(fixture.Device.Id);

        fixture.Edge.Verify(service => service.EnqueueAutomationAsync(
            It.IsAny<EnqueueEdgeCommandRequest>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.Pump.Status.Should().Be("unsafe");
    }

    [Fact]
    public async Task FreshConsistentFloats_QueueExpectedRelayCommand()
    {
        var fixture = CreateFixture("valid");

        await fixture.Service.ApplyAutoRelaysAsync(fixture.Device.Id);

        fixture.Edge.Verify(service => service.EnqueueAutomationAsync(
            It.Is<EnqueueEdgeCommandRequest>(request =>
                request.DeviceCode == fixture.Device.DeviceCode
                && request.Command == "on"
                && request.Channel == "1"),
            fixture.AreaId,
            "ras-auto-relay",
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Pump.Status.Should().Be("active");
    }

    private static Fixture CreateFixture(
        string scenario,
        EdgeCommand? command = null)
    {
        var areaId = Guid.NewGuid();
        var system = new WaterSystem
        {
            Id = Guid.NewGuid(),
            FarmingAreaId = areaId,
            Type = "RAS"
        };
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "edge-a",
            FarmingAreaId = areaId,
            Status = scenario == "controller-offline"
                ? DeviceStatus.Offline
                : DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var pump = new RasComponent
        {
            Id = Guid.NewGuid(),
            WaterSystemId = system.Id,
            RelayDeviceId = device.Id,
            RelayChannel = "1",
            HasRelay = true,
            ControlMode = "auto",
            Status = "active",
            ParamDefaultsJson =
                """
                {
                  "auto": {
                    "onSensor": "float_low",
                    "onValue": 1,
                    "offSensor": "float_high",
                    "offValue": 1,
                    "firmwareInterlock": true
                  }
                }
                """
        };
        var low = new Sensor
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            WaterSystemId = system.Id,
            SensorCode = "float_low",
            SensorType = "Float",
            IsActive = scenario != "inactive-float",
            LastSeenAt = scenario == "stale"
                ? DateTime.UtcNow.AddMinutes(-5)
                : DateTime.UtcNow
        };
        var high = new Sensor
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            WaterSystemId = system.Id,
            SensorCode = "float_high",
            SensorType = "Float",
            IsActive = true,
            LastSeenAt = scenario == "stale"
                ? DateTime.UtcNow.AddMinutes(-5)
                : DateTime.UtcNow
        };
        var measuredAt = scenario == "stale"
            ? DateTime.UtcNow.AddMinutes(-5)
            : scenario == "future"
                ? DateTime.UtcNow.AddMinutes(5)
            : DateTime.UtcNow;
        var measurements = new[]
        {
            new WaterMeasurement
            {
                SensorId = low.Id,
                Value = 1,
                MeasuredAt = measuredAt
            },
            new WaterMeasurement
            {
                SensorId = high.Id,
                Value = scenario == "conflict" ? 1 : 0,
                MeasuredAt = measuredAt
            }
        };
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(value => value.Devices).Returns(Repository(device).Object);
        uow.SetupGet(value => value.WaterSystems).Returns(Repository(system).Object);
        uow.SetupGet(value => value.RasComponents).Returns(Repository(pump).Object);
        uow.SetupGet(value => value.Sensors).Returns(Repository(low, high).Object);
        uow.SetupGet(value => value.WaterMeasurements)
            .Returns(Repository(measurements).Object);
        uow.SetupGet(value => value.EdgeCommands).Returns(
            command is null
                ? Repository<EdgeCommand>().Object
                : Repository(command).Object);
        uow.Setup(value => value.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var edge = new Mock<IEdgeCommandService>();
        edge.Setup(service => service.EnqueueAutomationAsync(
                It.IsAny<EnqueueEdgeCommandRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<EdgeCommandDto>.Ok(null!));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RasAutomation:FirmwareInterlockVerified"] = "true",
                ["RasAutomation:DefaultSensorFreshnessSeconds"] = "30",
                ["RasAutomation:RelayStateTimeoutSeconds"] = "30"
            })
            .Build();
        var service = new RasFlowService(
            uow.Object, edge.Object, null, configuration);
        return new Fixture(
            service, edge, pump, device, areaId);
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] rows)
        where T : class
    {
        var data = rows.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(repo => repo.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                data.FirstOrDefault(item =>
                    (Guid?)item.GetType().GetProperty("Id")?.GetValue(item)
                    == id));
        repository.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                Expression<Func<T, bool>> predicate,
                CancellationToken _) =>
                data.AsQueryable().Where(predicate).ToList());
        return repository;
    }

    private sealed record Fixture(
        RasFlowService Service,
        Mock<IEdgeCommandService> Edge,
        RasComponent Pump,
        Device Device,
        Guid AreaId);
}
