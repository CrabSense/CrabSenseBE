using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class Phase2HardwareSafetyTests
{
    [Fact]
    public async Task ManualCommand_WithoutActor_IsRejectedWithoutQueueWrite()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-a", FarmingAreaId = Guid.NewGuid(),
            Status = DeviceStatus.Online, EdgeState = "Online"
        };
        var commands = Repository<EdgeCommand>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        uow.Setup(x => x.EdgeCommands).Returns(commands.Object);

        var act = () => new EdgeCommandService(uow.Object).EnqueueManualAsync(
            new EnqueueEdgeCommandRequest("esp-a", "alloff"), Guid.Empty);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(401);
        commands.Verify(
            x => x.AddAsync(It.IsAny<EdgeCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Manager_A_CannotEnqueue_Device_B()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-b", FarmingAreaId = areaB,
            Status = DeviceStatus.Online, EdgeState = "Online"
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        uow.Setup(x => x.Users).Returns(Repository(new AppUser
        {
            Id = userId, Role = UserRole.FarmManager, IsActive = true
        }).Object);
        uow.Setup(x => x.FarmManagerAssignments).Returns(Repository(
            new FarmManagerAssignment { ManagerUserId = userId, FarmingAreaId = areaA }).Object);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);

        var act = () => new EdgeCommandService(uow.Object, current.Object).EnqueueManualAsync(
            new EnqueueEdgeCommandRequest("esp-b", "alloff"), userId);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task TrustedAutomation_WithMatchingArea_IsQueuedWithSource()
    {
        var areaId = Guid.NewGuid();
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-auto", FarmingAreaId = areaId,
            Status = DeviceStatus.Online, EdgeState = "Approved"
        };
        EdgeCommand? saved = null;
        var commands = Repository<EdgeCommand>();
        commands.Setup(x => x.AddAsync(
                It.IsAny<EdgeCommand>(), It.IsAny<CancellationToken>()))
            .Callback<EdgeCommand, CancellationToken>((command, _) => saved = command)
            .Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        uow.Setup(x => x.EdgeCommands).Returns(commands.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        await new EdgeCommandService(uow.Object).EnqueueAutomationAsync(
            new EnqueueEdgeCommandRequest("esp-auto", "alloff"),
            areaId,
            "water-failsafe");

        saved.Should().NotBeNull();
        saved!.RequestedByUserId.Should().BeNull();
        saved.RequestSource.Should().Be("automation:water-failsafe");
    }

    [Fact]
    public async Task Device_A_CannotAcknowledge_Command_B()
    {
        var command = new EdgeCommand
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-b", Command = "off",
            Status = "delivered", LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1)
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.EdgeCommands).Returns(Repository(command).Object);

        var act = () => new EdgeCommandService(uow.Object).AcknowledgeAsync(
            command.Id, new EdgeCommandAckRequest("acknowledged"), "esp-a");

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task EdgeIdempotencyKey_CannotBeReusedForDifferentPayload()
    {
        var areaId = Guid.NewGuid();
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-idempotent",
            FarmingAreaId = areaId,
            Status = DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var existing = new EdgeCommand
        {
            DeviceCode = device.DeviceCode,
            Command = "off",
            CorrelationId = "same-key",
            RequestSource = "automation:scheduler",
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        uow.Setup(x => x.EdgeCommands).Returns(Repository(existing).Object);
        var service = new EdgeCommandService(uow.Object);

        var act = () => service.EnqueueAutomationAsync(
            new EnqueueEdgeCommandRequest(
                device.DeviceCode, "alloff", IdempotencyKey: "same-key"),
            areaId,
            "scheduler");

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task StaleDeliveryAttempt_CannotAcknowledgeNewLease()
    {
        var currentAttempt = Guid.NewGuid();
        var command = new EdgeCommand
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-a",
            Command = "off",
            Status = "delivered",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(1),
            DeliveryAttemptId = currentAttempt,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.EdgeCommands).Returns(Repository(command).Object);
        var service = new EdgeCommandService(uow.Object);

        var act = () => service.AcknowledgeAsync(
            command.Id,
            new EdgeCommandAckRequest(
                "acknowledged",
                DeliveryAttemptId: Guid.NewGuid()),
            command.DeviceCode);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(409);
        command.Status.Should().Be("delivered");
        uow.Verify(x => x.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CommandAtRetryLimit_IsTimedOutAndNotRedelivered()
    {
        var command = new EdgeCommand
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-a",
            Command = "off",
            Status = "pending",
            AttemptCount = 3,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.EdgeCommands).Returns(Repository(command).Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await new EdgeCommandService(uow.Object)
            .GetPendingAsync(command.DeviceCode, "device:test");

        result.Data.Should().BeEmpty();
        command.Status.Should().Be("timedout");
        command.ResultMessage.Should().Contain("Maximum delivery attempts");
    }

    [Fact]
    public async Task ProvisionedDevice_WithoutCredential_IsRejected()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-1", CredentialHash = "ABC",
            EdgeState = "Online", Status = DeviceStatus.Online
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        var context = new DefaultHttpContext();
        var config = new ConfigurationBuilder().Build();

        var result = await EdgeKeyGuard.CheckDeviceAsync(
            context.Request, config, uow.Object, device.DeviceCode, CancellationToken.None);

        result.Denied.Should().NotBeNull();
        (result.Denied as Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult).Should().NotBeNull();
    }

    [Fact]
    public async Task PendingDevice_IsRejectedEvenWithLegacyFleetKey()
    {
        const string fleetKey = "legacy-fleet-key";
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-pending",
            EdgeState = "Pending",
            Status = DeviceStatus.Offline
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        var context = new DefaultHttpContext();
        context.Request.Headers[EdgeKeyGuard.HeaderName] = fleetKey;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [EdgeKeyGuard.ConfigKey] = fleetKey,
                ["EdgeSync:AllowLegacySharedKey"] = "true"
            })
            .Build();

        var result = await EdgeKeyGuard.CheckDeviceAsync(
            context.Request, config, uow.Object, device.DeviceCode,
            CancellationToken.None);

        result.Identity.Should().BeNull();
        result.Denied.Should()
            .BeOfType<Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task RevokedDevice_IsRejectedEvenWithItsPreviousCredential()
    {
        const string secret = "old-device-secret";
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-revoked",
            CredentialHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret))),
            EdgeState = "Revoked",
            Status = DeviceStatus.Offline
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        var context = new DefaultHttpContext();
        context.Request.Headers[EdgeKeyGuard.DeviceHeaderName] = secret;

        var result = await EdgeKeyGuard.CheckDeviceAsync(
            context.Request,
            new ConfigurationBuilder().Build(),
            uow.Object,
            device.DeviceCode,
            CancellationToken.None);

        result.Denied.Should().BeOfType<Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult>();
        result.Identity.Should().BeNull();
    }

    [Fact]
    public async Task HardwareCleaningAck_WithoutResult_DoesNotComplete()
    {
        var run = new WaterAnalysisRun
        {
            Id = Guid.NewGuid(),
            ExecutionMode = "hardware",
            Status = "running",
            CurrentStep = 8,
            AwaitingEvent = "CLEANING_COMPLETED",
            ControllerId = "esp-1",
            Analyte = "NO2"
        };
        var runs = Repository(run);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.WaterAnalysisRuns).Returns(runs.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var current = Mock.Of<ICurrentUserService>();
        var options = Microsoft.Extensions.Options.Options.Create(
            new CrabSenseBE.Application.Options.WaterAnalysisOptions());
        var service = new WaterAnalysisService(uow.Object, current, options);

        await service.RecordHardwareEventAsync(run.Id,
            new WaterAnalysisHardwareEventRequest("esp-1", "CLEANING_COMPLETED"));

        run.Status.Should().Be("failed");
        run.CleaningConfirmedAt.Should().NotBeNull();
        run.No2.Should().BeNull();
    }

    [Fact]
    public async Task HardwareResult_RequiresValidatedModelEvidence()
    {
        var run = new WaterAnalysisRun
        {
            Id = Guid.NewGuid(),
            ExecutionMode = "hardware",
            Status = "running",
            CurrentStep = 6,
            AwaitingEvent = "ANALYSIS_RESULT_SAVED",
            ControllerId = "esp-1",
            Analyte = "NO2"
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.WaterAnalysisRuns).Returns(Repository(run).Object);
        var service = new WaterAnalysisService(
            uow.Object,
            Mock.Of<ICurrentUserService>(),
            Microsoft.Extensions.Options.Options.Create(
                new CrabSenseBE.Application.Options.WaterAnalysisOptions()));

        var act = () => service.RecordHardwareEventAsync(
            run.Id,
            new WaterAnalysisHardwareEventRequest(
                "esp-1",
                "ANALYSIS_RESULT_SAVED",
                0.25m,
                1.2m,
                "http://insecure/image.jpg",
                ModelResultId: "model-run-1"));

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(400);
        run.No2.Should().BeNull();
        run.ResultRecordedAt.Should().BeNull();
    }

    [Fact]
    public async Task HardwareResult_PreservesExistingHardwareSnapshot()
    {
        var run = new WaterAnalysisRun
        {
            Id = Guid.NewGuid(), ExecutionMode = "hardware",
            Status = "running", CurrentStep = 6,
            AwaitingEvent = "ANALYSIS_RESULT_SAVED",
            ControllerId = "esp-1", Analyte = "NO2",
            HardwareJson = "{\"ControllerId\":\"esp-1\",\"CameraId\":\"cam-1\",\"SamplePumpId\":\"pump-1\"}"
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.WaterAnalysisRuns).Returns(Repository(run).Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var service = new WaterAnalysisService(uow.Object,
            Mock.Of<ICurrentUserService>(),
            Microsoft.Extensions.Options.Options.Create(new CrabSenseBE.Application.Options.WaterAnalysisOptions()));
        await service.RecordHardwareEventAsync(run.Id, new WaterAnalysisHardwareEventRequest(
            "esp-1", "ANALYSIS_RESULT_SAVED", 0.25m, 0.99m,
            "https://example.com/sample.jpg", ModelResultId: "model-run-1"));
        using var evidence = System.Text.Json.JsonDocument.Parse(run.HardwareJson!);
        evidence.RootElement.GetProperty("ControllerId").GetString().Should().Be("esp-1");
        evidence.RootElement.GetProperty("CameraId").GetString().Should().Be("cam-1");
        evidence.RootElement.GetProperty("SamplePumpId").GetString().Should().Be("pump-1");
        evidence.RootElement.GetProperty("modelResultId").GetString().Should().Be("model-run-1");
        run.No2.Should().Be(0.25m);
    }

    [Fact]
    public async Task HardwareEvent_AfterDeadline_IsRejected()
    {
        var run = new WaterAnalysisRun
        {
            Id = Guid.NewGuid(), ExecutionMode = "hardware",
            Status = "stopping", CurrentStep = 8,
            AwaitingEvent = "CLEANING_COMPLETED", ControllerId = "esp-1",
            StepDeadlineAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.WaterAnalysisRuns).Returns(Repository(run).Object);
        var service = new WaterAnalysisService(uow.Object,
            Mock.Of<ICurrentUserService>(),
            Microsoft.Extensions.Options.Options.Create(new CrabSenseBE.Application.Options.WaterAnalysisOptions()));
        var act = () => service.RecordHardwareEventAsync(run.Id,
            new WaterAnalysisHardwareEventRequest("esp-1", "CLEANING_COMPLETED"));
        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        run.Status.Should().Be("stopping");
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HardwareStart_WithoutCommandDispatcher_ReturnsNotImplemented()
    {
        var userId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.Users).Returns(Repository(new AppUser
        {
            Id = userId,
            IsActive = true,
            Role = UserRole.FarmManager
        }).Object);
        uow.SetupGet(x => x.FarmManagerAssignments).Returns(Repository(
            new FarmManagerAssignment
            {
                ManagerUserId = userId,
                FarmingAreaId = areaId
            }).Object);
        uow.SetupGet(x => x.FarmingAreas).Returns(Repository(
            new FarmingArea { Id = areaId, Name = "A" }).Object);
        uow.SetupGet(x => x.WaterAnalysisRuns)
            .Returns(Repository<WaterAnalysisRun>().Object);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        var service = new WaterAnalysisService(
            uow.Object,
            current.Object,
            Microsoft.Extensions.Options.Options.Create(
                new CrabSenseBE.Application.Options.WaterAnalysisOptions()));

        var act = () => service.StartAsync(
            areaId,
            new StartWaterAnalysisRequest(
                "NO2", null, null, null, "hardware"));

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(501);
        uow.Verify(x => x.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Kiosk_A_CannotUpdate_Device_OwnedBy_Kiosk_B()
    {
        const string secret = "kiosk-a-secret";
        var area = Guid.NewGuid();
        var kioskA = new FarmKiosk { Id = Guid.NewGuid(), FarmingAreaId = area, Status = "Online" };
        var kioskB = new FarmKiosk { Id = Guid.NewGuid(), FarmingAreaId = area, Status = "Online" };
        var credential = new KioskCredential
        {
            Id = Guid.NewGuid(),
            KioskId = kioskA.Id,
            SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))
        };
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-owned", KioskId = kioskB.Id,
            FarmingAreaId = area, EdgeState = "Online"
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.KioskCredentials).Returns(Repository(credential).Object);
        uow.Setup(x => x.FarmKiosks).Returns(Repository(kioskA, kioskB).Object);
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);

        var act = () => new KioskProvisioningService(
                uow.Object, Mock.Of<ICurrentUserService>())
            .RegisterControllerAsync(secret,
                new CrabSenseBE.Application.DTOs.Kiosk.RegisterControllerRequest(
                    device.DeviceCode, null, "1.0", null, "10.0.0.5"));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(409);
        device.IpAddress.Should().BeNull();
    }

    [Fact]
    public async Task Device_A_CannotIngestSensorOwnedByDevice_B_OrReassignIt()
    {
        var deviceA = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-a",
            Status = DeviceStatus.Online, EdgeState = "Online"
        };
        var deviceB = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-b",
            Status = DeviceStatus.Online, EdgeState = "Online"
        };
        var sensorB = new Sensor
        {
            Id = Guid.NewGuid(), SensorCode = "ph-b", DeviceId = deviceB.Id,
            SensorType = "pH", Unit = "pH", IsActive = true
        };
        var measurements = Repository<WaterMeasurement>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(deviceA, deviceB).Object);
        uow.Setup(x => x.Sensors).Returns(Repository(sensorB).Object);
        uow.Setup(x => x.WaterMeasurements).Returns(measurements.Object);
        var service = new IotService(
            uow.Object,
            Mock.Of<IStorageService>(),
            Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance);

        var act = () => service.IngestSensorDataAsync(
            new SensorDataRequest(deviceA.DeviceCode, "ph-b", 7.8m, "pH", DateTime.UtcNow),
            deviceA.Id,
            deviceA.DeviceCode,
            CancellationToken.None);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        sensorB.DeviceId.Should().Be(deviceB.Id);
        measurements.Verify(
            x => x.AddAsync(It.IsAny<WaterMeasurement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AuthenticatedDevice_CanIngestItsOwnRegisteredSensor()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-valid",
            Status = DeviceStatus.Online, EdgeState = "Approved"
        };
        var sensor = new Sensor
        {
            Id = Guid.NewGuid(), SensorCode = "ph-valid", DeviceId = device.Id,
            SensorType = "pH", Unit = "pH", IsActive = true
        };
        var measurements = Repository<WaterMeasurement>();
        measurements.Setup(x => x.AddAsync(
                It.IsAny<WaterMeasurement>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        uow.Setup(x => x.Sensors).Returns(Repository(sensor).Object);
        uow.Setup(x => x.WaterMeasurements).Returns(measurements.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = new IotService(
            uow.Object,
            Mock.Of<IStorageService>(),
            Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance);

        var result = await service.IngestSensorDataAsync(
            new SensorDataRequest(
                device.DeviceCode, sensor.SensorCode, 7.2m, "pH", DateTime.UtcNow),
            device.Id,
            device.DeviceCode);

        result.Success.Should().BeTrue();
        measurements.Verify(x => x.AddAsync(
            It.Is<WaterMeasurement>(row => row.SensorId == sensor.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Batch_WithForeignSensor_IsRejectedBeforeAnyMeasurementIsWritten()
    {
        var deviceA = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-a",
            Status = DeviceStatus.Online, EdgeState = "Approved"
        };
        var deviceB = new Device
        {
            Id = Guid.NewGuid(), DeviceCode = "esp-b",
            Status = DeviceStatus.Online, EdgeState = "Approved"
        };
        var sensorA = new Sensor
        {
            Id = Guid.NewGuid(), SensorCode = "ph-a", DeviceId = deviceA.Id,
            SensorType = "pH", Unit = "pH", IsActive = true
        };
        var sensorB = new Sensor
        {
            Id = Guid.NewGuid(), SensorCode = "ph-b", DeviceId = deviceB.Id,
            SensorType = "pH", Unit = "pH", IsActive = true
        };
        var measurements = Repository<WaterMeasurement>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(deviceA, deviceB).Object);
        uow.Setup(x => x.Sensors).Returns(Repository(sensorA, sensorB).Object);
        uow.Setup(x => x.WaterMeasurements).Returns(measurements.Object);
        var service = new IotService(
            uow.Object,
            Mock.Of<IStorageService>(),
            Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance);

        var act = () => service.IngestSensorDataBatchAsync(
            new SensorDataBatchRequest(new[]
            {
                new SensorDataRequest("esp-a", "ph-a", 7.1m, "pH", DateTime.UtcNow),
                new SensorDataRequest("esp-a", "ph-b", 7.2m, "pH", DateTime.UtcNow)
            }),
            deviceA.Id,
            deviceA.DeviceCode);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        measurements.Verify(
            x => x.AddAsync(It.IsAny<WaterMeasurement>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeviceCredential_CannotAuthenticateHeartbeatForAnotherDeviceId()
    {
        const string secretA = "device-a-secret";
        const string secretB = "device-b-secret";
        var deviceA = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-a",
            CredentialHash = Hash(secretA),
            Status = DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var deviceB = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-b",
            CredentialHash = Hash(secretB),
            Status = DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(deviceA, deviceB).Object);
        var context = new DefaultHttpContext();
        context.Request.Headers[EdgeKeyGuard.DeviceHeaderName] = secretB;

        var result = await EdgeKeyGuard.CheckDeviceAsync(
            context.Request,
            new ConfigurationBuilder().Build(),
            uow.Object,
            deviceA.Id,
            CancellationToken.None);

        result.Identity.Should().BeNull();
        result.Denied.Should()
            .BeOfType<Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Hdf5Upload_CannotUseAuthenticatedIdentityForAnotherDevice()
    {
        var deviceA = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-a",
            Status = DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var storage = new Mock<IStorageService>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(deviceA).Object);
        var service = new IotService(
            uow.Object,
            storage.Object,
            Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance);
        var request = new Hdf5UploadRequest(
            "esp-b",
            "chunk.h5",
            "checksum",
            3,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow);

        var act = () => service.ProcessHdf5UploadAsync(
            request,
            new MemoryStream([1, 2, 3]),
            deviceA.Id,
            deviceA.DeviceCode);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(403);
        storage.Verify(x => x.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Hdf5Upload_WithFalseChecksum_IsRejectedBeforeStorage()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-a",
            Status = DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var storage = new Mock<IStorageService>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(device).Object);
        var service = new IotService(
            uow.Object,
            storage.Object,
            Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance);
        var request = new Hdf5UploadRequest(
            device.DeviceCode,
            "chunk.h5",
            new string('0', 64),
            3,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow);

        var act = () => service.ProcessHdf5UploadAsync(
            request,
            new MemoryStream([1, 2, 3]),
            device.Id,
            device.DeviceCode);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(400);
        storage.Verify(x => x.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FirstRelayState_IsObservationNotFalseTransitionAlert()
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "edge-a",
            EdgeState = "Approved",
            Status = DeviceStatus.Online
        };
        var node = new RasComponent
        {
            Id = Guid.NewGuid(),
            RelayDeviceId = device.Id,
            RelayChannel = "1",
            IsOn = false,
            LastReportedAt = null
        };
        var alerts = new Mock<IAlertService>();
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(x => x.Devices).Returns(Repository(device).Object);
        uow.SetupGet(x => x.RasComponents).Returns(Repository(node).Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = new IotService(
            uow.Object,
            Mock.Of<IStorageService>(),
            alerts.Object,
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance);

        await service.ReportRelayAsync(device.DeviceCode, 1, true);

        node.IsOn.Should().BeTrue();
        node.LastReportedAt.Should().NotBeNull();
        alerts.Verify(alert => alert.RaiseOnceAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<IReadOnlyDictionary<string, string?>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        alerts.Verify(alert => alert.ClearAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_A_CannotRead_Device_B_Details()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var deviceB = new Device
        {
            Id = Guid.NewGuid(),
            DeviceCode = "esp-b",
            FarmingAreaId = areaB,
            Status = DeviceStatus.Online,
            EdgeState = "Approved"
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(Repository(deviceB).Object);
        uow.Setup(x => x.Users).Returns(Repository(new AppUser
        {
            Id = userId,
            Role = UserRole.FarmManager,
            IsActive = true
        }).Object);
        uow.Setup(x => x.FarmManagerAssignments).Returns(Repository(
            new FarmManagerAssignment
            {
                ManagerUserId = userId,
                FarmingAreaId = areaA
            }).Object);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        var service = new IotService(
            uow.Object,
            Mock.Of<IStorageService>(),
            Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(),
            NullLogger<IotService>.Instance,
            current.Object);

        var act = () => service.GetDeviceAsync(deviceB.Id);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Water_snapshot_is_read_only_and_ignores_failed_measurements()
    {
        var area = Guid.NewGuid();
        var user = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var completed = new WaterAnalysisRun
        {
            FarmingAreaId = area, Status = "completed", No2 = 0.02m,
            StartedAt = now.AddMinutes(-10), CompletedAt = now.AddMinutes(-9)
        };
        var failed = new WaterAnalysisRun
        {
            FarmingAreaId = area, Status = "failed", No2 = 9m,
            StartedAt = now.AddMinutes(-5), CompletedAt = now.AddMinutes(-4)
        };
        var active = new WaterAnalysisRun
        {
            FarmingAreaId = area, Status = "running", ExecutionMode = "simulation",
            CurrentStep = 1, StartedAt = now.AddMinutes(-2), LastStepAt = now.AddMinutes(-2)
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(Repository(new AppUser
            { Id = user, IsActive = true, Role = UserRole.FarmManager }).Object);
        uow.Setup(x => x.FarmManagerAssignments).Returns(Repository(new FarmManagerAssignment
            { ManagerUserId = user, FarmingAreaId = area }).Object);
        uow.Setup(x => x.FarmingAreas).Returns(Repository(new FarmingArea { Id = area, Name = "A" }).Object);
        uow.Setup(x => x.WaterAnalysisRuns).Returns(Repository(completed, failed, active).Object);
        uow.Setup(x => x.Devices).Returns(Repository(new Device
            { FarmingAreaId = area, DeviceType = "esp32", DeviceCode = "esp-a", Status = DeviceStatus.Online }).Object);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(user);
        var service = new WaterAnalysisService(uow.Object, current.Object,
            Microsoft.Extensions.Options.Options.Create(new CrabSenseBE.Application.Options.WaterAnalysisOptions()));
        var result = await service.GetSnapshotAsync(area);
        result.Data!.Latest!.Metrics.Single(x => x.Code == "no2").Value.Should().Be(0.02m);
        active.CurrentStep.Should().Be(1);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Historical_ai_detection_without_crab_does_not_adopt_current_occupant()
    {
        var area = Guid.NewGuid();
        var row = new FarmingRow { FarmingAreaId = area };
        var box = new Box { FarmingRowId = row.Id };
        var crab = new Crab { BoxId = box.Id, Status = CrabStatus.Alive, Code = "NEW" };
        var old = new AiDetection
        {
            BoxId = box.Id, DetectedAt = DateTime.UtcNow.AddDays(-1),
            ModelVersion = "crabsense-heuristic-v1", Status = "completed"
        };
        var user = Guid.NewGuid();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(Repository(new AppUser
            { Id = user, IsActive = true, Role = UserRole.FarmManager }).Object);
        uow.Setup(x => x.FarmManagerAssignments).Returns(Repository(new FarmManagerAssignment
            { ManagerUserId = user, FarmingAreaId = area }).Object);
        uow.Setup(x => x.AiDetections).Returns(Repository(old).Object);
        uow.Setup(x => x.FarmingRows).Returns(Repository(row).Object);
        uow.Setup(x => x.Boxes).Returns(Repository(box).Object);
        uow.Setup(x => x.Crabs).Returns(Repository(crab).Object);
        uow.Setup(x => x.Devices).Returns(Repository<Device>().Object);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(user);
        var result = await new AiOpsService(uow.Object, current.Object)
            .ListDetectionsAsync(box.Id, null);
        result.Data!.Single().CrabId.Should().BeNull();
        result.Data!.Single().CrabTag.Should().BeNull();
    }

    private static string Hash(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static Mock<IRepository<T>> Repository<T>(params T[] rows) where T : class
    {
        var data = rows.ToList();
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                data.FirstOrDefault(item =>
                    (Guid?)item.GetType().GetProperty("Id")?.GetValue(item) == id));
        repo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                data.AsQueryable().FirstOrDefault(predicate));
        repo.Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                data.AsQueryable().Where(predicate).ToList());
        repo.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(data);
        return repo;
    }
}
