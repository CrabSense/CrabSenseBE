using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class NonSalesAuditSafetyTests
{
    [Theory]
    [InlineData("999")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("Inbound,Outbound")]
    public async Task Frozen_transaction_rejects_noncanonical_type_before_database(string type)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var service = new FrozenStorageService(uow.Object, Mock.Of<ICurrentUserService>());
        var request = new CreateFrozenStorageTransactionRequest(Guid.NewGuid(), type, 1, 0.1m, null);
        Func<Task> act = () => service.CreateTransactionAsync(request);
        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(400);
        uow.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("999", "Online", 400)]
    [InlineData("Maintenance", "Online", 400)]
    [InlineData("Online", "Maintenance", 403)]
    public async Task Device_cannot_mutate_administrative_state(string reported, string existing, int expected)
    {
        var device = new Device { Id = Guid.NewGuid(), Status = Enum.Parse<DeviceStatus>(existing) };
        var repo = new Mock<IRepository<Device>>();
        repo.Setup(x => x.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(repo.Object);
        var service = new IotService(uow.Object, Mock.Of<IStorageService>(), Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(), NullLogger<IotService>.Instance);
        var request = new UpdateDeviceStatusRequest(reported, null, null);
        Func<Task> act = () => service.UpdateDeviceStatusAsync(device.Id, request);
        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(expected);
        device.Status.Should().Be(Enum.Parse<DeviceStatus>(existing));
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    [Theory]
    [InlineData("Online")]
    [InlineData("online")]
    [InlineData("0")]
    public async Task Admin_cannot_mark_camera_online_without_observation(string status)
    {
        var device = new Device { Id = Guid.NewGuid(), DeviceType = "camera", Status = DeviceStatus.Offline };
        var repo = new Mock<IRepository<Device>>();
        repo.Setup(x => x.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Devices).Returns(repo.Object);
        var service = new IotService(uow.Object, Mock.Of<IStorageService>(), Mock.Of<IAlertService>(),
            Mock.Of<IRasFlowService>(), NullLogger<IotService>.Instance);
        var req = new UpdateDeviceRequest(null, null, status, null, null, null);
        Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => service.UpdateDeviceAsync(device.Id, req))).StatusCode);
        Assert.Equal(DeviceStatus.Offline, device.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

}
