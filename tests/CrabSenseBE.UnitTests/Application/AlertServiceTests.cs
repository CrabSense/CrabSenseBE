using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: vượt ngưỡng → Alert; disconnect → Alert.</summary>
public class AlertServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<INotificationService> _notify = new();

    private AlertService Create() => new(_uow.Object, _notify.Object);

    [Fact]
    public async Task EvaluateMeasurement_WhenOutOfRange_CreatesAlert()
    {
        var sensor = new Sensor
        {
            Id = Guid.NewGuid(),
            SensorCode = "TEMP-01",
            SensorType = "Temperature",
            MinThreshold = 24,
            MaxThreshold = 30
        };

        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        alertRepo.Setup(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppUser { Id = Guid.NewGuid(), Role = UserRole.FarmOwner, IsActive = true } });

        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
        _uow.Setup(u => u.Users).Returns(userRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _notify.Setup(n => n.NotifyUsersAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Create().EvaluateMeasurementAsync(sensor, 35m);

        alertRepo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.TriggerValue == 35m && a.Status == AlertStatus.Active), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyUsersAsync(
            It.IsAny<IEnumerable<Guid>>(), "CrabSense Alert", It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateMeasurement_WhenInRange_DoesNothing()
    {
        var sensor = new Sensor
        {
            Id = Guid.NewGuid(),
            SensorCode = "PH-01",
            SensorType = "pH",
            MinThreshold = 7,
            MaxThreshold = 8.5m
        };
        var alertRepo = new Mock<IRepository<Alert>>();
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);

        await Create().EvaluateMeasurementAsync(sensor, 7.5m);

        alertRepo.Verify(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateThreshold_WhenMinGteMax_Throws()
    {
        await Assert.ThrowsAsync<CrabSenseBE.Application.Common.AppException>(() =>
            Create().CreateThresholdAsync(new CreateAlertThresholdRequest("DO", 10, 5)));
    }

    [Fact]
    public async Task CheckDisconnects_CreatesAlertForStaleSensor()
    {
        var sensor = new Sensor
        {
            Id = Guid.NewGuid(),
            SensorCode = "DO-01",
            IsActive = true,
            LastSeenAt = DateTime.UtcNow.AddMinutes(-60)
        };

        var sensorRepo = new Mock<IRepository<Sensor>>();
        sensorRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Sensor, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { sensor });

        var deviceRepo = new Mock<IRepository<Device>>();
        deviceRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Device, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<Device>());

        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        alertRepo.Setup(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AppUser>());

        _uow.Setup(u => u.Sensors).Returns(sensorRepo.Object);
        _uow.Setup(u => u.Devices).Returns(deviceRepo.Object);
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
        _uow.Setup(u => u.Users).Returns(userRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CheckDisconnectsAsync(15);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(1);
    }
}
