using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
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
    private FarmHistoryService Create() => new(
        _uow.Object, Mock.Of<IPublicImageStorage>(), currentUser: null);

    [Fact]
    public async Task AllocateCrab_MovesCrabAndOpensAllocation()
    {
        var crabId = Guid.NewGuid();
        var oldBox = Guid.NewGuid();
        var newBox = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var crab = new Crab
        {
            Id = crabId,
            CrabLotId = lotId,
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

        var crabStatusHistRepo = new Mock<IRepository<CrabStatusHistory>>();
        crabStatusHistRepo.Setup(r => r.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logRepo = new Mock<IRepository<OperationLog>>();
        logRepo.Setup(r => r.AddAsync(It.IsAny<OperationLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var lotAreas = new Mock<IRepository<CrabLotArea>>();
        lotAreas.Setup(r => r.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<CrabLotArea, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        _uow.Setup(u => u.CrabBoxAllocations).Returns(allocRepo.Object);
        _uow.Setup(u => u.BoxStatusHistories).Returns(statusHistRepo.Object);
        _uow.Setup(u => u.CrabStatusHistories).Returns(crabStatusHistRepo.Object);
        _uow.Setup(u => u.OperationLogs).Returns(logRepo.Object);
        _uow.Setup(u => u.CrabLotAreas).Returns(lotAreas.Object);
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

    [Theory]
    [InlineData(CrabStatus.Dead)]
    [InlineData(CrabStatus.Harvested)]
    [InlineData(CrabStatus.Sold)]
    [InlineData(CrabStatus.Missing)]
    [InlineData(CrabStatus.Quarantined)]
    public async Task Molting_cannot_resurrect_or_clear_quarantine(CrabStatus status)
    {
        var crab = new Crab { Id = Guid.NewGuid(), Status = status, WeightGram = 200 };
        var repo = new Mock<IRepository<Crab>>();
        repo.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        _uow.Setup(x => x.Crabs).Returns(repo.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => Create().CreateMoltingAsync(
            new CreateMoltingRecordRequest(crab.Id, null, null, 120m, "success", "manual", null)));
        ex.StatusCode.Should().Be(409);
        crab.Status.Should().Be(status);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Molting_cannot_claim_another_box()
    {
        var crab = new Crab { Id = Guid.NewGuid(), BoxId = Guid.NewGuid() };
        var repo = new Mock<IRepository<Crab>>();
        repo.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        _uow.Setup(x => x.Crabs).Returns(repo.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => Create().CreateMoltingAsync(
            new CreateMoltingRecordRequest(crab.Id, Guid.NewGuid(), null, 120m, "success", "manual", null)));
        ex.StatusCode.Should().Be(400);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Staff_cannot_submit_molt_for_outside_crab_using_own_box()
    {
        var staffId = Guid.NewGuid(); var areaA = Guid.NewGuid(); var areaB = Guid.NewGuid();
        var rowA = Guid.NewGuid(); var rowB = Guid.NewGuid(); var boxA = Guid.NewGuid(); var boxB = Guid.NewGuid();
        var crab = new Crab { Id = Guid.NewGuid(), BoxId = boxB };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(boxA, It.IsAny<CancellationToken>())).ReturnsAsync(new Box { Id = boxA, FarmingRowId = rowA });
        boxes.Setup(r => r.GetByIdAsync(boxB, It.IsAny<CancellationToken>())).ReturnsAsync(new Box { Id = boxB, FarmingRowId = rowB });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.GetByIdAsync(rowA, It.IsAny<CancellationToken>())).ReturnsAsync(new FarmingRow { Id = rowA, FarmingAreaId = areaA });
        rows.Setup(r => r.GetByIdAsync(rowB, It.IsAny<CancellationToken>())).ReturnsAsync(new FarmingRow { Id = rowB, FarmingAreaId = areaB });
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(r => r.GetByIdAsync(staffId, It.IsAny<CancellationToken>())).ReturnsAsync(new AppUser { Id = staffId, IsActive = true, Role = UserRole.FarmStaff });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment { StaffUserId = staffId, FarmingAreaId = areaA, ManagerUserId = Guid.NewGuid() });
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(staffId);
        _uow.Setup(x => x.Crabs).Returns(crabs.Object);
        _uow.Setup(x => x.Boxes).Returns(boxes.Object);
        _uow.Setup(x => x.FarmingRows).Returns(rows.Object);
        _uow.Setup(x => x.Users).Returns(users.Object);
        _uow.Setup(x => x.FarmStaffAssignments).Returns(assignments.Object);
        var service = new FarmHistoryService(_uow.Object, Mock.Of<IPublicImageStorage>(), current.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => service.CreateMoltingAsync(
            new CreateMoltingRecordRequest(crab.Id, boxA, null, 120m, "success", "manual", null)));
        ex.StatusCode.Should().Be(403);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task VoidMolting_KeepsRecordAndWritesCorrection()
    {
        var crabId = Guid.NewGuid();
        var record = new MoltingRecord
        {
            Id = Guid.NewGuid(),
            CrabId = crabId,
            Result = "success",
            Source = "manual"
        };
        var crab = new Crab { Id = crabId, MoltedAt = DateTime.UtcNow };
        var molts = new Mock<IRepository<MoltingRecord>>();
        molts.Setup(repo => repo.GetByIdAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        molts.Setup(repo => repo.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<MoltingRecord, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { record });
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(repo => repo.GetByIdAsync(crabId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crab);
        var corrections = new Mock<IRepository<CrabEventCorrection>>();
        CrabEventCorrection? savedCorrection = null;
        corrections.Setup(repo => repo.AddAsync(
                It.IsAny<CrabEventCorrection>(), It.IsAny<CancellationToken>()))
            .Callback<CrabEventCorrection, CancellationToken>((item, _) => savedCorrection = item)
            .Returns(Task.CompletedTask);
        _uow.Setup(unit => unit.MoltingRecords).Returns(molts.Object);
        _uow.Setup(unit => unit.Crabs).Returns(crabs.Object);
        _uow.Setup(unit => unit.CrabEventCorrections).Returns(corrections.Object);
        _uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().VoidMoltingAsync(
            record.Id, new VoidCrabEventRequest("Bản ghi trùng"));

        result.Success.Should().BeTrue();
        record.IsVoided.Should().BeTrue();
        record.VoidReason.Should().Be("Bản ghi trùng");
        savedCorrection.Should().NotBeNull();
        savedCorrection!.Action.Should().Be("void");
        molts.Verify(repo => repo.Remove(It.IsAny<MoltingRecord>()), Times.Never);
    }

    [Fact]
    public async Task Allocation_correction_is_audited_and_confirmed_history_cannot_be_deleted()
    {
        var allocation = new CrabBoxAllocation
        {
            Id = Guid.NewGuid(),
            CrabId = Guid.NewGuid(),
            BoxId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-1),
            Notes = "original"
        };
        var allocations = new Mock<IRepository<CrabBoxAllocation>>();
        allocations.Setup(repo => repo.GetByIdAsync(
                allocation.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allocation);
        var corrections = new Mock<IRepository<CrabEventCorrection>>();
        CrabEventCorrection? savedCorrection = null;
        corrections.Setup(repo => repo.AddAsync(
                It.IsAny<CrabEventCorrection>(), It.IsAny<CancellationToken>()))
            .Callback<CrabEventCorrection, CancellationToken>(
                (item, _) => savedCorrection = item)
            .Returns(Task.CompletedTask);
        _uow.Setup(unit => unit.CrabBoxAllocations).Returns(allocations.Object);
        _uow.Setup(unit => unit.CrabEventCorrections).Returns(corrections.Object);
        _uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var service = Create();

        var updated = await service.UpdateAllocationAsync(
            allocation.Id,
            new UpdateAllocationRequest(
                allocation.StartTime,
                allocation.EndTime,
                "Sửa thời gian theo nhật ký thiết bị"));

        updated.Success.Should().BeTrue();
        savedCorrection.Should().NotBeNull();
        savedCorrection!.EventType.Should().Be("allocation");
        savedCorrection.Action.Should().Be("update");
        savedCorrection.PreviousDataJson.Should().Contain("original");

        await Assert.ThrowsAsync<AppException>(
            () => service.DeleteAllocationAsync(allocation.Id));
        allocations.Verify(
            repo => repo.Remove(It.IsAny<CrabBoxAllocation>()), Times.Never);
        _uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
