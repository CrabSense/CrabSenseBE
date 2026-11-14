using System.Security.Cryptography;
using System.Text;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class KioskProvisioningScopeTests
{
    [Fact]
    public async Task Farm_user_cannot_list_kiosks_from_another_area()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var (uow, current) = Fixture(userId, UserRole.FarmStaff, areaA);
        var service = new KioskProvisioningService(uow.Object, current.Object);

        var act = () => service.ListAsync(areaB);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        uow.Verify(
            unit => unit.FarmKiosks,
            Times.Never);
    }

    [Fact]
    public async Task System_administrator_can_read_technical_kiosk_data()
    {
        var userId = Guid.NewGuid();
        var area = Guid.NewGuid();
        var (uow, current) = Fixture(
            userId, UserRole.SystemAdministrator, assignedArea: null);
        var kiosks = new Mock<IRepository<FarmKiosk>>();
        kiosks.Setup(repo => repo.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmKiosk, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FarmKiosk>());
        var codes = new Mock<IRepository<KioskProvisioningCode>>();
        codes.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<KioskProvisioningCode>());
        uow.Setup(unit => unit.FarmKiosks).Returns(kiosks.Object);
        uow.Setup(unit => unit.KioskProvisioningCodes).Returns(codes.Object);
        var service = new KioskProvisioningService(uow.Object, current.Object);

        var result = await service.ListAsync(area);

        result.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSession_is_read_only_and_does_not_touch_last_seen()
    {
        const string secret = "kiosk-secret";
        var areaId = Guid.NewGuid();
        var kiosk = new FarmKiosk
        {
            Id = Guid.NewGuid(),
            Code = "KIOSK-001",
            FarmingAreaId = areaId,
            Status = "Offline",
            LastSeenAt = DateTime.UtcNow.AddHours(-1)
        };
        var originalLastSeen = kiosk.LastSeenAt;
        var credentials = new Mock<IRepository<KioskCredential>>();
        credentials.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<KioskCredential, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KioskCredential
            {
                KioskId = kiosk.Id,
                SecretHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(secret)))
            });
        var kiosks = new Mock<IRepository<FarmKiosk>>();
        kiosks.Setup(repo => repo.GetByIdAsync(kiosk.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kiosk);
        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(repo => repo.GetByIdAsync(areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingArea { Id = areaId, Code = "AREA-A", Name = "Khu A" });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.KioskCredentials).Returns(credentials.Object);
        uow.Setup(unit => unit.FarmKiosks).Returns(kiosks.Object);
        uow.Setup(unit => unit.FarmingAreas).Returns(areas.Object);
        var service = new KioskProvisioningService(
            uow.Object, Mock.Of<ICurrentUserService>());

        var result = await service.GetSessionAsync(secret);

        result.Data!.Status.Should().Be("Offline");
        kiosk.LastSeenAt.Should().Be(originalLastSeen);
        uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_IsIdempotent_AndDoesNotMarkPendingControllerOnline()
    {
        var areaId = Guid.NewGuid();
        var (uow, current) = Fixture(Guid.NewGuid(), UserRole.SystemAdministrator, null);
        var kiosk = new FarmKiosk { FarmingAreaId = areaId, Code = "KIOSK-TEST" };
        var device = new Device { DeviceCode = "ESP-TEST", KioskId = kiosk.Id, FarmingAreaId = areaId, EdgeState = "Pending" };
        var devices = new Mock<IRepository<Device>>();
        devices.Setup(r => r.GetByIdAsync(device.Id, It.IsAny<CancellationToken>())).ReturnsAsync(device);
        var kiosks = new Mock<IRepository<FarmKiosk>>();
        kiosks.Setup(r => r.GetByIdAsync(kiosk.Id, It.IsAny<CancellationToken>())).ReturnsAsync(kiosk);
        uow.Setup(x => x.Devices).Returns(devices.Object);
        uow.Setup(x => x.FarmKiosks).Returns(kiosks.Object);
        var service = new KioskProvisioningService(uow.Object, current.Object);
        var first = await service.ApproveControllerAsync(device.Id);
        first.Data!.EdgeState.Should().Be("Approved");
        device.Status.Should().Be(DeviceStatus.Offline);
        var hash = device.CredentialHash;
        var secret = device.PendingSecret;
        hash.Should().NotBeNullOrWhiteSpace();
        await service.ApproveControllerAsync(device.Id);
        device.CredentialHash.Should().Be(hash);
        device.PendingSecret.Should().Be(secret);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeKiosk_RevokesAssociatedDeviceCredentials()
    {
        var (uow, current) = Fixture(Guid.NewGuid(), UserRole.SystemAdministrator, null);
        var kiosk = new FarmKiosk { FarmingAreaId = Guid.NewGuid(), Code = "KIOSK-TEST" };
        var device = new Device { DeviceCode = "ESP-TEST", KioskId = kiosk.Id, EdgeState = "Online",
            CredentialHash = "test-hash", PendingSecret = "test-pending", Status = DeviceStatus.Online };
        var devices = new Mock<IRepository<Device>>();
        devices.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Device,bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] {device});
        var kiosks = new Mock<IRepository<FarmKiosk>>();
        kiosks.Setup(r => r.GetByIdAsync(kiosk.Id, It.IsAny<CancellationToken>())).ReturnsAsync(kiosk);
        var codes = new Mock<IRepository<KioskProvisioningCode>>();
        codes.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<KioskProvisioningCode,bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KioskProvisioningCode>());
        var credentials = new Mock<IRepository<KioskCredential>>();
        credentials.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<KioskCredential,bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KioskCredential>());
        uow.Setup(x => x.Devices).Returns(devices.Object);
        uow.Setup(x => x.FarmKiosks).Returns(kiosks.Object);
        uow.Setup(x => x.KioskProvisioningCodes).Returns(codes.Object);
        uow.Setup(x => x.KioskCredentials).Returns(credentials.Object);
        var service = new KioskProvisioningService(uow.Object, current.Object);
        await service.RevokeAsync(kiosk.Id);
        kiosk.Status.Should().Be("Revoked");
        device.EdgeState.Should().Be("Revoked");
        device.CredentialHash.Should().BeNull();
        device.PendingSecret.Should().BeNull();
        device.Status.Should().Be(DeviceStatus.Offline);
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Revoked")]
    [InlineData("Approved")]
    public async Task Registration_DoesNotRestoreBlockedStateOrClaimOnline(string state)
    {
        const string key = "kiosk-test-key";
        var kiosk = new FarmKiosk { FarmingAreaId = Guid.NewGuid(), Code = "KIOSK-TEST" };
        var device = new Device { DeviceCode = "ESP-TEST", KioskId = kiosk.Id,
            FarmingAreaId = kiosk.FarmingAreaId, EdgeState = state, Status = DeviceStatus.Offline };
        var uow = new Mock<IUnitOfWork>();
        var credentials = new Mock<IRepository<KioskCredential>>();
        credentials.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<KioskCredential,bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KioskCredential { KioskId = kiosk.Id, SecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) });
        var kiosks = new Mock<IRepository<FarmKiosk>>();
        kiosks.Setup(r => r.GetByIdAsync(kiosk.Id, It.IsAny<CancellationToken>())).ReturnsAsync(kiosk);
        var devices = new Mock<IRepository<Device>>();
        devices.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Device,bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(device);
        uow.Setup(x => x.KioskCredentials).Returns(credentials.Object);
        uow.Setup(x => x.FarmKiosks).Returns(kiosks.Object);
        uow.Setup(x => x.Devices).Returns(devices.Object);
        var service = new KioskProvisioningService(uow.Object, Mock.Of<ICurrentUserService>());
        var result = await service.RegisterControllerAsync(key, new CrabSenseBE.Application.DTOs.Kiosk.RegisterControllerRequest(device.DeviceCode,null,null,null,null));
        result.Data!.EdgeState.Should().Be(state);
        device.Status.Should().Be(DeviceStatus.Offline);
        device.LastSeenAt.Should().BeNull();
        if (state != "Approved") uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (
        Mock<IUnitOfWork> Uow,
        Mock<ICurrentUserService> Current) Fixture(
        Guid userId,
        UserRole role,
        Guid? assignedArea)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(userId);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = userId,
                Username = "scope-user",
                Role = role,
                IsActive = true
            });
        var staffAssignments = new Mock<IRepository<FarmStaffAssignment>>();
        staffAssignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<
                    Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignedArea is Guid area
                ? new FarmStaffAssignment
                {
                    StaffUserId = userId,
                    ManagerUserId = Guid.NewGuid(),
                    FarmingAreaId = area
                }
                : null);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments)
            .Returns(staffAssignments.Object);
        return (uow, current);
    }
}
