using System.Text.Json;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public class FarmOperationFollowsCrabTests
{
    [Fact]
    public async Task ListByBox_IncludesOpsOfCrabNowInThatBox()
    {
        var crabId = Guid.NewGuid();
        var oldBox = Guid.NewGuid();
        var newBox = Guid.NewGuid();
        var op = new FarmOperation
        {
            Id = Guid.NewGuid(),
            Type = "feeding",
            BoxIdsJson = JsonSerializer.Serialize(new[] { oldBox.ToString() }),
            CrabIdsJson = JsonSerializer.Serialize(new[] { crabId.ToString() }),
            Appetite = "many",
            Timestamp = DateTime.UtcNow.AddHours(-2)
        };

        var ops = new Mock<IRepository<FarmOperation>>();
        ops.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { op });

        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new Crab { Id = crabId, BoxId = newBox } });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmOperations).Returns(ops.Object);
        uow.Setup(u => u.Crabs).Returns(crabs.Object);
        var current = ConfigureScope(uow, newBox);

        var result = await new FarmOperationService(uow.Object, current.Object)
            .ListByBoxAsync(newBox);

        result.Data.Should().ContainSingle(o => o.Id == op.Id);
    }

    [Fact]
    public async Task ListByCrab_IncludesLegacyBoxOnlyOpsWhileCrabLivedThere()
    {
        var crabId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var op = new FarmOperation
        {
            Id = Guid.NewGuid(),
            Type = "inspection",
            BoxIdsJson = JsonSerializer.Serialize(new[] { boxId.ToString() }),
            CrabIdsJson = "[]",
            Timestamp = DateTime.UtcNow.AddHours(-3)
        };

        var ops = new Mock<IRepository<FarmOperation>>();
        ops.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { op });

        var allocs = new Mock<IRepository<CrabBoxAllocation>>();
        allocs.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CrabBoxAllocation
                {
                    CrabId = crabId,
                    BoxId = boxId,
                    StartTime = DateTime.UtcNow.AddDays(-2),
                    EndTime = DateTime.UtcNow.AddHours(-1)
                }
            });

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.FarmOperations).Returns(ops.Object);
        uow.Setup(u => u.CrabBoxAllocations).Returns(allocs.Object);
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(repo => repo.GetByIdAsync(crabId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Crab { Id = crabId });
        uow.Setup(unit => unit.Crabs).Returns(crabs.Object);
        var current = ConfigureScope(uow, boxId);

        var result = await new FarmOperationService(uow.Object, current.Object)
            .ListByCrabAsync(crabId);

        result.Data.Should().ContainSingle(o => o.Id == op.Id);
    }

    private static Mock<ICurrentUserService> ConfigureScope(
        Mock<IUnitOfWork> uow,
        Guid boxId)
    {
        var userId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(userId);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = userId, Username = "staff", Role = UserRole.FarmStaff, IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = userId, FarmingAreaId = areaId
            });
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxId, FarmingRowId = rowId, Code = "BOX" });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(rowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowId, FarmingAreaId = areaId, Code = "ROW" });
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        return current;
    }
}
