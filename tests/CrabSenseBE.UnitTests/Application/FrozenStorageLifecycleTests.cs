using System.Linq.Expressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.FrozenStorage;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class FrozenStorageLifecycleTests
{
    [Theory]
    [InlineData(HarvestStatus.PendingApproval)]
    [InlineData(HarvestStatus.Approved)]
    [InlineData(HarvestStatus.InProgress)]
    public async Task CreateFrozenLot_rejects_harvest_before_final_confirmation(
        HarvestStatus status)
    {
        var (service, uow, request) = Fixture(status);

        var act = () => service.CreateAsync(request);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateFrozenLot_accepts_completed_harvest_in_assigned_area()
    {
        var (service, uow, request) = Fixture(HarvestStatus.Completed);

        var result = await service.CreateAsync(request);

        result.Success.Should().BeTrue();
        result.Data!.HarvestVoucherId.Should().Be(request.HarvestVoucherId);
        uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        FrozenStorageService Service,
        Mock<IUnitOfWork> Uow,
        CreateFrozenLotRequest Request) Fixture(HarvestStatus status)
    {
        var areaId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var harvestDate = DateTime.UtcNow.Date;
        var vouchers = new Mock<IRepository<HarvestVoucher>>();
        vouchers.Setup(repo => repo.GetByIdAsync(
                voucherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HarvestVoucher
            {
                Id = voucherId,
                VoucherCode = "HAR-FROZEN",
                FarmingAreaId = areaId,
                HarvestDate = harvestDate,
                Status = status
            });
        var lots = new Mock<IRepository<FrozenLot>>();
        lots.Setup(repo => repo.AnyAsync(
                It.IsAny<Expression<Func<FrozenLot, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        lots.Setup(repo => repo.AddAsync(
                It.IsAny<FrozenLot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = userId, Username = "staff", Role = UserRole.FarmStaff, IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = userId, FarmingAreaId = areaId
            });
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(userId);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.HarvestVouchers).Returns(vouchers.Object);
        uow.Setup(unit => unit.FrozenLots).Returns(lots.Object);
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var request = new CreateFrozenLotRequest(
            "FZ-COMPLETED",
            voucherId,
            harvestDate,
            harvestDate.AddMonths(6),
            0,
            "M",
            0,
            "Kho A",
            areaId);
        return (
            new FrozenStorageService(uow.Object, current.Object),
            uow,
            request);
    }
}
