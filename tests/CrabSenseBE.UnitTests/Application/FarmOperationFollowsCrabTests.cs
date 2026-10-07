using System.Text.Json;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
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

        var result = await new FarmOperationService(uow.Object)
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

        var result = await new FarmOperationService(uow.Object)
            .ListByCrabAsync(crabId);

        result.Data.Should().ContainSingle(o => o.Id == op.Id);
    }
}
