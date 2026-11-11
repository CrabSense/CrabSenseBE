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

public sealed class FrozenOpeningBalanceServiceTests
{
    [Fact]
    public async Task Verify_reconciles_opening_plus_movements_without_changing_current_stock()
    {
        var fixture = Fixture();

        var result = await fixture.Service.VerifyOpeningBalanceAsync(
            fixture.Balance.Id,
            new VerifyFrozenOpeningBalanceRequest(5, 2.5m, "Đã đối chiếu"));

        fixture.Balance.VerificationStatus.Should().Be(
            FrozenOpeningBalanceVerificationStatus.Verified);
        fixture.Balance.VerifiedByUserId.Should().Be(fixture.ManagerId);
        fixture.Lot.Quantity.Should().Be(8);
        fixture.Lot.WeightKg.Should().Be(4m);
        result.Data!.RecordedQuantityDelta.Should().Be(3);
        result.Data.RecordedWeightDeltaKg.Should().Be(1.5m);
        result.Data.ExpectedCurrentQuantity.Should().Be(8);
        result.Data.ExpectedCurrentWeightKg.Should().Be(4m);
        result.Data.IsReconciled.Should().BeTrue();
        fixture.Uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Verify_rejects_inconsistent_opening_without_changing_database()
    {
        var fixture = Fixture();

        var act = () => fixture.Service.VerifyOpeningBalanceAsync(
            fixture.Balance.Id,
            new VerifyFrozenOpeningBalanceRequest(6, 2.5m, "Không khớp"));

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        fixture.Balance.VerificationStatus.Should().Be(
            FrozenOpeningBalanceVerificationStatus.NeedsReview);
        fixture.Balance.VerifiedByUserId.Should().BeNull();
        fixture.Lot.Quantity.Should().Be(8);
        fixture.Lot.WeightKg.Should().Be(4m);
        fixture.Uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static OpeningFixture Fixture()
    {
        var areaId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var lot = new FrozenLot
        {
            Id = Guid.NewGuid(),
            FarmingAreaId = areaId,
            LotCode = "LEGACY-FROZEN",
            Quantity = 8,
            WeightKg = 4m
        };
        var balance = new FrozenInventoryOpeningBalance
        {
            Id = Guid.NewGuid(),
            FrozenLotId = lot.Id,
            FarmingAreaId = areaId,
            OpeningQuantity = 5,
            OpeningWeightKg = 2.5m,
            OpeningDate = DateTime.UtcNow,
            Source = "LegacyFrozenLotReconciliation",
            VerificationStatus = FrozenOpeningBalanceVerificationStatus.NeedsReview
        };
        var adjustment = new FrozenInventoryAdjustment
        {
            FrozenLotId = lot.Id,
            FarmingAreaId = areaId,
            QuantityDelta = 3,
            WeightDeltaKg = 1.5m
        };

        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(managerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = managerId,
                Username = "manager",
                Role = UserRole.FarmManager,
                IsActive = true
            });
        var assignments = new Mock<IRepository<FarmManagerAssignment>>();
        assignments.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmManagerAssignment
                {
                    ManagerUserId = managerId,
                    FarmingAreaId = areaId
                }
            });
        var lots = new Mock<IRepository<FrozenLot>>();
        lots.Setup(repo => repo.GetByIdAsync(lot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lot);
        var balances = new Mock<IRepository<FrozenInventoryOpeningBalance>>();
        balances.Setup(repo => repo.GetByIdAsync(
                balance.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        balances.Setup(repo => repo.Update(balance));
        var adjustments = new Mock<IRepository<FrozenInventoryAdjustment>>();
        adjustments.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<FrozenInventoryAdjustment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { adjustment });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmManagerAssignments).Returns(assignments.Object);
        uow.Setup(unit => unit.FrozenLots).Returns(lots.Object);
        uow.Setup(unit => unit.FrozenInventoryOpeningBalances).Returns(balances.Object);
        uow.Setup(unit => unit.FrozenInventoryAdjustments).Returns(adjustments.Object);
        uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(managerId);

        return new OpeningFixture(
            new FrozenStorageService(uow.Object, current.Object),
            uow,
            lot,
            balance,
            managerId);
    }

    private sealed record OpeningFixture(
        FrozenStorageService Service,
        Mock<IUnitOfWork> Uow,
        FrozenLot Lot,
        FrozenInventoryOpeningBalance Balance,
        Guid ManagerId);
}
