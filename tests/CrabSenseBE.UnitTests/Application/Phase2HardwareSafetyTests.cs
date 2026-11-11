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
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class Phase2HardwareSafetyTests
{
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

        var act = () => new EdgeCommandService(uow.Object, current.Object).EnqueueAsync(
            new EnqueueEdgeCommandRequest("esp-b", "alloff"), userId);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
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
