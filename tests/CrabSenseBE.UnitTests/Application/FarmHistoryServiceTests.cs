using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: phân bổ cua + ghi lột xác.</summary>
public class FarmHistoryServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private FarmHistoryService Create() => new(_uow.Object);

    [Fact]
    public async Task AllocateCrab_MovesCrabAndOpensAllocation()
    {
        var crabId = Guid.NewGuid();
        var oldBox = Guid.NewGuid();
        var newBox = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var crab = new Crab
        {
            Id = crabId,
            Status = CrabStatus.Alive,
            BoxAllocations = new List<CrabBoxAllocation>
    {
        new() { CrabId = crabId, BoxId = oldBox, StartTime = DateTime.UtcNow }
    }
        }; var box = new Box { Id = newBox, FarmingRowId = rowId, Code = "B2", Status = "empty", IsOccupied = false };
        var row = new FarmingRow { Id = rowId, FarmingAreaId = areaId, Name = "R1", IsActive = true };
        var area = new FarmingArea { Id = areaId, Name = "A1", IsActive = true };

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        crabRepo.Setup(r => r.Update(It.IsAny<Crab>()));
        crabRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(newBox, It.IsAny<CancellationToken>())).ReturnsAsync(box);
        boxRepo.Setup(r => r.GetByIdAsync(oldBox, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = oldBox, FarmingRowId = rowId, Code = "B1", IsOccupied = true });
        boxRepo.Setup(r => r.Update(It.IsAny<Box>()));

        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetByIdAsync(rowId, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetByIdAsync(areaId, It.IsAny<CancellationToken>())).ReturnsAsync(area);

        var allocRepo = new Mock<IRepository<CrabBoxAllocation>>();
        allocRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<CrabBoxAllocation>());
        allocRepo.Setup(r => r.AddAsync(It.IsAny<CrabBoxAllocation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var statusHistRepo = new Mock<IRepository<BoxStatusHistory>>();
        statusHistRepo.Setup(r => r.AddAsync(It.IsAny<BoxStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logRepo = new Mock<IRepository<OperationLog>>();
        logRepo.Setup(r => r.AddAsync(It.IsAny<OperationLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        _uow.Setup(u => u.CrabBoxAllocations).Returns(allocRepo.Object);
        _uow.Setup(u => u.BoxStatusHistories).Returns(statusHistRepo.Object);
        _uow.Setup(u => u.OperationLogs).Returns(logRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().AllocateCrabAsync(new AllocateCrabRequest(crabId, newBox, areaId, rowId, "stock"));

        result.Success.Should().BeTrue();
        crab.BoxAllocations.Should().Contain(a => a.BoxId == newBox); box.IsOccupied.Should().BeTrue();
        box.Status.Should().Be("active");
    }

    [Fact]
    public async Task CreateMolting_UpdatesCrabSoftshell()
    {
        var crabId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var crab = new Crab
        {
            Id = crabId,
            WeightGram = 100,
            BoxAllocations = new List<CrabBoxAllocation>
    {
        new() { CrabId = crabId, BoxId = boxId, StartTime = DateTime.UtcNow }
    }
        }; var box = new Box { Id = boxId, Status = "active", IsOccupied = true };
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        crabRepo.Setup(r => r.Update(It.IsAny<Crab>()));

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>())).ReturnsAsync(box);
        boxRepo.Setup(r => r.Update(It.IsAny<Box>()));

        var moltRepo = new Mock<IRepository<MoltingRecord>>();
        moltRepo.Setup(r => r.AddAsync(It.IsAny<MoltingRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var statusHistRepo = new Mock<IRepository<BoxStatusHistory>>();
        statusHistRepo.Setup(r => r.AddAsync(It.IsAny<BoxStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var crabStatusRepo = new Mock<IRepository<CrabStatusHistory>>();
        crabStatusRepo.Setup(r => r.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var weightRepo = new Mock<IRepository<CrabWeightHistory>>();
        weightRepo.Setup(r => r.AddAsync(It.IsAny<CrabWeightHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.MoltingRecords).Returns(moltRepo.Object);
        _uow.Setup(u => u.BoxStatusHistories).Returns(statusHistRepo.Object);
        _uow.Setup(u => u.CrabStatusHistories).Returns(crabStatusRepo.Object);
        _uow.Setup(u => u.CrabWeightHistories).Returns(weightRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CreateMoltingAsync(new CreateMoltingRecordRequest(
            crabId, null, null, 120m, "success", "manual", null));

        result.Success.Should().BeTrue();
        crab.MoltingStage.Should().Be("softshell");
        crab.Condition.Should().Be(CrabCondition.Softshell);
        crab.WeightGram.Should().Be(120m);
        crab.MoltedAt.Should().NotBeNull();
        box.Status.Should().Be("molting");
    }

    [Fact]
    public async Task CreateMolting_AcceptsDesktopNormalResult()
    {
        var crabId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var crab = new Crab
        {
            Id = crabId,
            BoxId = boxId,
            WeightGram = 100
        };
        var box = new Box { Id = boxId, Status = "active", IsOccupied = true };
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        crabRepo.Setup(r => r.Update(It.IsAny<Crab>()));

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>())).ReturnsAsync(box);
        boxRepo.Setup(r => r.Update(It.IsAny<Box>()));

        var moltRepo = new Mock<IRepository<MoltingRecord>>();
        moltRepo.Setup(r => r.AddAsync(It.IsAny<MoltingRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var statusHistRepo = new Mock<IRepository<BoxStatusHistory>>();
        statusHistRepo.Setup(r => r.AddAsync(It.IsAny<BoxStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var crabStatusRepo = new Mock<IRepository<CrabStatusHistory>>();
        crabStatusRepo.Setup(r => r.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.MoltingRecords).Returns(moltRepo.Object);
        _uow.Setup(u => u.BoxStatusHistories).Returns(statusHistRepo.Object);
        _uow.Setup(u => u.CrabStatusHistories).Returns(crabStatusRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CreateMoltingAsync(new CreateMoltingRecordRequest(
            crabId, null, null, null, "normal", "desktop", null));

        result.Success.Should().BeTrue();
        crab.MoltingStage.Should().Be("softshell");
        crab.Condition.Should().Be(CrabCondition.Softshell);
    }

    [Fact]
    public async Task CreateMolting_AllowsCrabWithoutBox()
    {
        var crabId = Guid.NewGuid();
        var crab = new Crab { Id = crabId, WeightGram = 90 };
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        crabRepo.Setup(r => r.Update(It.IsAny<Crab>()));

        var moltRepo = new Mock<IRepository<MoltingRecord>>();
        moltRepo.Setup(r => r.AddAsync(It.IsAny<MoltingRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var crabStatusRepo = new Mock<IRepository<CrabStatusHistory>>();
        crabStatusRepo.Setup(r => r.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.MoltingRecords).Returns(moltRepo.Object);
        _uow.Setup(u => u.CrabStatusHistories).Returns(crabStatusRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CreateMoltingAsync(new CreateMoltingRecordRequest(
            crabId, null, null, null, "success", "desktop", null));

        result.Success.Should().BeTrue();
        crab.Condition.Should().Be(CrabCondition.Softshell);
    }

    [Fact]
    public async Task AllocateCrab_WhenCrabMissing_ThrowsNotFound()
    {
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Crab?)null);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().AllocateCrabAsync(new AllocateCrabRequest(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null)));
    }
}
