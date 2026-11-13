using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;
using System.Linq.Expressions;

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
        alertRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());
        alertRepo.Setup(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppUser { Id = Guid.NewGuid(), Role = UserRole.FarmAdministrator, IsActive = true } });

        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
        _uow.Setup(u => u.Users).Returns(userRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _notify.Setup(n => n.NotifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, string?>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Create().EvaluateMeasurementAsync(sensor, 35m);

        alertRepo.Verify(r => r.AddAsync(It.Is<Alert>(a =>
            a.TriggerValue == 35m && a.Status == AlertStatus.Active), It.IsAny<CancellationToken>()), Times.Once);
        _notify.Verify(n => n.NotifyAsync(
            NotificationTypeCodes.SensorThreshold, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            nameof(AlertSeverity.Warning), It.IsAny<IReadOnlyDictionary<string, string?>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
    public async Task UpdateThreshold_WhenMinGteMax_ThrowsBeforePersisting()
    {
        await Assert.ThrowsAsync<CrabSenseBE.Application.Common.AppException>(() =>
            Create().UpdateThresholdAsync(
                Guid.NewGuid(),
                new UpdateAlertThresholdRequest(10, 10, "Warning", true)));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        alertRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());
        alertRepo.Setup(r => r.AddAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<AppUser, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<AppUser>());

        var kioskRepo = new Mock<IRepository<FarmKiosk>>();
        kioskRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmKiosk, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<FarmKiosk>());

        _uow.Setup(u => u.Sensors).Returns(sensorRepo.Object);
        _uow.Setup(u => u.Devices).Returns(deviceRepo.Object);
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
        _uow.Setup(u => u.Users).Returns(userRepo.Object);
        _uow.Setup(u => u.FarmKiosks).Returns(kioskRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CheckDisconnectsAsync(15);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(1);
    }

    [Fact]
    public async Task ThresholdSelection_DoesNotLeakBetweenAreas()
    {
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var areaC = Guid.NewGuid();
        var wsA = new WaterSystem { Id = Guid.NewGuid(), FarmingAreaId = areaA };
        var wsB = new WaterSystem { Id = Guid.NewGuid(), FarmingAreaId = areaB };
        var wsC = new WaterSystem { Id = Guid.NewGuid(), FarmingAreaId = areaC };
        var thresholdA = new AlertThreshold
        {
            Id = Guid.NewGuid(), SensorType = "pH", MinValue = 6, MaxValue = 7,
            FarmingAreaId = areaA, IsActive = true
        };
        var thresholdB = new AlertThreshold
        {
            Id = Guid.NewGuid(), SensorType = "pH", MinValue = 7, MaxValue = 8,
            FarmingAreaId = areaB, IsActive = true
        };
        var global = new AlertThreshold
        {
            Id = Guid.NewGuid(), SensorType = "pH", MinValue = 6.5m, MaxValue = 8.5m,
            IsActive = true
        };
        var thresholds = Repository(thresholdA, thresholdB, global);
        var systems = Repository(wsA, wsB, wsC);
        var alerts = Repository<Alert>();
        var captured = new List<Alert>();
        alerts.Setup(repo => repo.AddAsync(
                It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Callback<Alert, CancellationToken>((alert, _) => captured.Add(alert))
            .Returns(Task.CompletedTask);
        _uow.SetupGet(uow => uow.AlertThresholds).Returns(thresholds.Object);
        _uow.SetupGet(uow => uow.WaterSystems).Returns(systems.Object);
        _uow.SetupGet(uow => uow.Alerts).Returns(alerts.Object);
        _uow.Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _notify.Setup(service => service.NotifyAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<string?>(),
                It.IsAny<IReadOnlyDictionary<string, string?>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Create().EvaluateMeasurementAsync(new Sensor
        {
            Id = Guid.NewGuid(), SensorCode = "PH-A", SensorType = "pH",
            WaterSystemId = wsA.Id
        }, 7.5m);
        await Create().EvaluateMeasurementAsync(new Sensor
        {
            Id = Guid.NewGuid(), SensorCode = "PH-C", SensorType = "pH",
            WaterSystemId = wsC.Id
        }, 9m);

        captured.Should().HaveCount(2);
        captured[0].AlertThresholdId.Should().Be(thresholdA.Id);
        captured[0].FarmingAreaId.Should().Be(areaA);
        captured[1].AlertThresholdId.Should().Be(global.Id);
        captured[1].FarmingAreaId.Should().Be(areaC);
        captured.Should().NotContain(alert =>
            alert.AlertThresholdId == thresholdB.Id);
    }

    [Fact]
    public async Task RaiseOnce_AcknowledgedIncident_RemainsSameOpenIncident()
    {
        var sourceId = Guid.NewGuid().ToString("N");
        var existing = new Alert
        {
            Id = Guid.NewGuid(),
            IncidentType = "tank_overflow",
            SourceType = "RasComponent",
            SourceId = sourceId,
            Status = AlertStatus.Acknowledged,
            OccurrenceCount = 1
        };
        var alerts = Repository(existing);
        _uow.SetupGet(uow => uow.Alerts).Returns(alerts.Object);
        _uow.Setup(uow => uow.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await Create().RaiseOnceAsync(
            "tank_overflow",
            "RasComponent",
            sourceId,
            "Tank overflow");

        existing.OccurrenceCount.Should().Be(2);
        existing.Status.Should().Be(AlertStatus.Acknowledged);
        alerts.Verify(repo => repo.AddAsync(
            It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
        _notify.Verify(service => service.NotifyAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<Guid?>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyDictionary<string, string?>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Acknowledge_or_start_retry_does_not_regress_in_progress_incident()
    {
        var at = DateTime.UtcNow.AddMinutes(-10);
        var alert = new Alert { Id = Guid.NewGuid(), Status = AlertStatus.InProgress,
            ProcessingStartedAt = at, ProcessingBy = Guid.NewGuid() };
        var actor = alert.ProcessingBy;
        _uow.SetupGet(x => x.Alerts).Returns(Repository(alert).Object);
        _uow.SetupGet(x => x.AlertThresholds).Returns(Repository<AlertThreshold>().Object);
        await Create().AcknowledgeAsync(alert.Id, null!);
        await Create().StartProcessingAsync(alert.Id, null);
        alert.Status.Should().Be(AlertStatus.InProgress);
        alert.ProcessingStartedAt.Should().Be(at);
        alert.ProcessingBy.Should().Be(actor);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_DeviceRecoveredWithoutTelemetry_IsRejected()
    {
        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            IncidentType = "device_disconnect",
            SourceType = "Device",
            SourceId = Guid.NewGuid().ToString("N"),
            Status = AlertStatus.Active
        };
        _uow.SetupGet(uow => uow.Alerts).Returns(Repository(alert).Object);
        _uow.SetupGet(uow => uow.Devices)
            .Returns(Repository<Device>().Object);

        var act = () => Create().ResolveAsync(
            alert.Id,
            new ResolveAlertRequest(
                null, null, null, DeviceRecovered: true));

        (await act.Should().ThrowAsync<
                CrabSenseBE.Application.Common.AppException>())
            .Which.StatusCode.Should().Be(409);
        _uow.Verify(uow => uow.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
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
}
