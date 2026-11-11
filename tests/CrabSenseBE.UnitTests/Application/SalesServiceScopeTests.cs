using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Sales;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class SalesServiceScopeTests
{
    [Fact]
    public async Task Sale_uses_database_box_area_and_authenticated_operator()
    {
        var userId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var (uow, current) = Fixture(userId, areaId);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxId, FarmingRowId = rowId });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(rowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowId, FarmingAreaId = areaId });
        var sales = new Mock<IRepository<SaleTransaction>>();
        SaleTransaction? saved = null;
        sales.Setup(repo => repo.AddAsync(
                It.IsAny<SaleTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<SaleTransaction, CancellationToken>((item, _) => saved = item)
            .Returns(Task.CompletedTask);
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        uow.Setup(unit => unit.SaleTransactions).Returns(sales.Object);
        uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = new SalesService(uow.Object, current.Object);

        var result = await service.CreateAsync(new CreateSaleRequest(
            "Buyer", 1, 100, FarmingAreaId: areaId, BoxId: boxId,
            OperatorId: Guid.NewGuid(), TotalAmount: 999_999));

        result.Success.Should().BeTrue();
        saved.Should().NotBeNull();
        saved!.FarmingAreaId.Should().Be(areaId);
        saved.OperatorId.Should().Be(userId);
        saved.TotalAmount.Should().Be(100);
    }

    [Fact]
    public async Task Sale_outside_assigned_area_is_rejected_without_saving()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        var boxB = Guid.NewGuid();
        var (uow, current) = Fixture(userId, areaA);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(boxB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxB, FarmingRowId = rowB });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(rowB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowB, FarmingAreaId = areaB });
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        var service = new SalesService(uow.Object, current.Object);

        var act = () => service.CreateAsync(new CreateSaleRequest(
            "Buyer", 1, 100, FarmingAreaId: areaB, BoxId: boxB));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Sales_order_id_from_another_area_is_rejected()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var (uow, current) = Fixture(userId, areaA);
        var order = new SalesOrder
        {
            Id = Guid.NewGuid(),
            OrderCode = "ORDER-B",
            FarmingAreaId = areaB
        };
        var orders = new Mock<IRepository<SalesOrder>>();
        orders.Setup(repo => repo.GetByIdAsync(
                order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        uow.Setup(unit => unit.SalesOrders).Returns(orders.Object);
        var service = new SalesOrderService(uow.Object, current.Object);

        var act = () => service.GetOrderByIdAsync(order.Id);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
    }

    private static (
        Mock<IUnitOfWork> Uow,
        Mock<ICurrentUserService> Current) Fixture(
        Guid userId,
        Guid areaId)
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
                Username = "staff",
                Role = UserRole.FarmStaff,
                IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<
                    Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = userId,
                ManagerUserId = Guid.NewGuid(),
                FarmingAreaId = areaId
            });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        return (uow, current);
    }
}
