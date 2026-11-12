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

    [Fact]
    public async Task Customer_list_excludes_other_areas_and_unassigned_legacy()
    {
        var area = Guid.NewGuid();
        var (uow, current) = Fixture(Guid.NewGuid(), area);
        var customers = new Mock<IRepository<Customer>>();
        var own = new Customer { Name = "Own", FarmingAreaId = area };
        var rows = new[] { own, new Customer { Name = "Other", FarmingAreaId = Guid.NewGuid() },
            new Customer { Name = "Legacy" } };
        customers.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Customer, bool>> predicate, CancellationToken _) =>
                rows.AsQueryable().Where(predicate).ToList());
        uow.Setup(x => x.Customers).Returns(customers.Object);
        var result = await new SalesOrderService(uow.Object, current.Object).GetCustomersAsync();
        result.Data!.Select(x => x.Id).Should().Equal(own.Id);
    }

    [Fact]
    public async Task Customer_update_cannot_target_another_area()
    {
        var area = Guid.NewGuid();
        var (uow, current) = Fixture(Guid.NewGuid(), area);
        var other = new Customer { Name = "Other", FarmingAreaId = Guid.NewGuid() };
        var customers = new Mock<IRepository<Customer>>();
        customers.Setup(x => x.GetByIdAsync(other.Id, It.IsAny<CancellationToken>())).ReturnsAsync(other);
        uow.Setup(x => x.Customers).Returns(customers.Object);
        var act = () => new SalesOrderService(uow.Object, current.Object).UpsertCustomerAsync(
            new UpsertCustomerRequest("Changed", FarmingAreaId: area, CustomerId: other.Id));
        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        other.Name.Should().Be("Other");
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Same_name_different_phone_creates_separate_customer()
    {
        var area = Guid.NewGuid();
        var (uow, current) = Fixture(Guid.NewGuid(), area);
        var customers = new Mock<IRepository<Customer>>();
        var existing = new Customer { Name = "Nguyen Van A", Phone = "0901", FarmingAreaId = area };
        customers.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<Customer, bool>> predicate, CancellationToken _) =>
                new[] { existing }.AsQueryable().Where(predicate).ToList());
        Customer? created = null;
        customers.Setup(x => x.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => created = c).Returns(Task.CompletedTask);
        uow.Setup(x => x.Customers).Returns(customers.Object);
        await new SalesOrderService(uow.Object, current.Object).UpsertCustomerAsync(
            new UpsertCustomerRequest(existing.Name, "0902", FarmingAreaId: area));
        existing.Phone.Should().Be("0901");
        created.Should().NotBeNull();
        created!.Phone.Should().Be("0902");
        created.FarmingAreaId.Should().Be(area);
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
