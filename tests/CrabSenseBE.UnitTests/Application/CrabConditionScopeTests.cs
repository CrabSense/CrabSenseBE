using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Condition;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class CrabConditionScopeTests
{
    [Fact]
    public async Task Staff_cannot_submit_condition_outside_assigned_area()
    {
        var staffId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        var boxB = Guid.NewGuid();
        var (uow, current) = ScopeFixture(staffId, areaA);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(boxB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxB, FarmingRowId = rowB });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(rowB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowB, FarmingAreaId = areaB });
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        var service = new CrabConditionService(uow.Object, current.Object);

        var act = () => service.SubmitAsync(new SubmitCrabConditionRequest(
            boxB, 200, CarapaceWidthCm: 8));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Staff_cannot_use_crab_id_from_another_box()
    {
        var staffId = Guid.NewGuid();
        var area = Guid.NewGuid();
        var row = Guid.NewGuid();
        var requestedBox = Guid.NewGuid();
        var crabBox = Guid.NewGuid();
        var crabId = Guid.NewGuid();
        var (uow, current) = ScopeFixture(staffId, area);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(requestedBox, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = requestedBox, FarmingRowId = row });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(row, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = row, FarmingAreaId = area });
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(repo => repo.GetByIdAsync(crabId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Crab
            {
                Id = crabId,
                BoxId = crabBox,
                Status = CrabStatus.Alive
            });
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        uow.Setup(unit => unit.Crabs).Returns(crabs.Object);
        var service = new CrabConditionService(uow.Object, current.Object);

        var act = () => service.SubmitAsync(new SubmitCrabConditionRequest(
            requestedBox, 200, CarapaceWidthCm: 8, CrabId: crabId));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(400);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static (
        Mock<IUnitOfWork> Uow,
        Mock<ICurrentUserService> Current) ScopeFixture(
        Guid staffId,
        Guid areaId)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(staffId);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(
                staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = staffId,
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
                StaffUserId = staffId,
                ManagerUserId = Guid.NewGuid(),
                FarmingAreaId = areaId
            });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        return (uow, current);
    }
}
