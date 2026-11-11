using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: HarvestService — Box ↔ Harvest link endpoints.</summary>
public class HarvestServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private HarvestService Create(Guid? userId = null)
    {
        _currentUser.Setup(c => c.UserId).Returns(userId ?? Guid.NewGuid());
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        return new HarvestService(_uow.Object, _currentUser.Object);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetBoxesByVoucherAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoxesByVoucher_VoucherNotFound_Throws()
    {
        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarvestVoucher?)null);

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetBoxesByVoucherAsync(Guid.NewGuid()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetVouchersByBoxAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVouchersByBox_BoxNotFound_Throws()
    {
        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Box?)null);

        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetVouchersByBoxAsync(Guid.NewGuid()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CreateAsync — existing tests validate the core CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_VoucherWithLines_Succeeds()
    {
        var currentUser = Guid.NewGuid();
        _currentUser.Setup(c => c.UserId).Returns(currentUser);
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxId = Guid.NewGuid();

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestVoucher, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        voucherRepo.Setup(r => r.AddAsync(It.IsAny<HarvestVoucher>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(r => r.AddAsync(It.IsAny<HarvestLine>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => new Crab
            {
                Id = id,
                Status = CrabStatus.Alive,
                StockedAt = DateTime.UtcNow,
                MoltingStage = "hard",
                BoxId = boxId,
                BoxAllocations = new List<CrabBoxAllocation>()
            });

        var lineRepoFind = new Mock<IRepository<HarvestLine>>();
        lineRepoFind.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestLine, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // ApplyHarvestToCrabsAsync closes open box allocations and writes history rows.
        var allocationRepo = new Mock<IRepository<CrabBoxAllocation>>();
        allocationRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<CrabBoxAllocation>());

        var statusHistoryRepo = new Mock<IRepository<CrabStatusHistory>>();
        statusHistoryRepo.Setup(r => r.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var harvestHistoryRepo = new Mock<IRepository<CrabHarvestHistory>>();
        harvestHistoryRepo.Setup(r => r.AddAsync(It.IsAny<CrabHarvestHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.HarvestLines).Returns(lineRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.CrabBoxAllocations).Returns(allocationRepo.Object);
        _uow.Setup(u => u.CrabStatusHistories).Returns(statusHistoryRepo.Object);
        _uow.Setup(u => u.CrabHarvestHistories).Returns(harvestHistoryRepo.Object);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(r => r.GetByIdAsync(currentUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = currentUser,
                Role = UserRole.FarmManager,
                IsActive = true,
                Username = "manager"
            });
        var assignments = new Mock<IRepository<FarmManagerAssignment>>();
        assignments.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmManagerAssignment
            {
                ManagerUserId = currentUser,
                FarmingAreaId = areaId
            });
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxId, FarmingRowId = rowId, Code = "BOX-1" });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.GetByIdAsync(rowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowId, FarmingAreaId = areaId, Code = "ROW-1" });
        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(r => r.GetByIdAsync(areaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingArea { Id = areaId, Code = "AREA-1", Name = "Khu 1" });
        _uow.Setup(u => u.Users).Returns(users.Object);
        _uow.Setup(u => u.FarmManagerAssignments).Returns(assignments.Object);
        _uow.Setup(u => u.Boxes).Returns(boxes.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rows.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areas.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var request = new CreateHarvestVoucherRequest(
            null,
            DateTime.UtcNow,
            "test harvest",
            new List<HarvestLineRequest>
            {
                new(Guid.NewGuid(), 250, "M", true, null)
            },
            FarmingAreaId: areaId);

        var result = await Create(currentUser).CreateAsync(request);

        result.Success.Should().BeTrue();
        result.Data!.VoucherCode.Should().StartWith("HAR-");
        result.Data.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task Workflow_enforces_two_area_scope_self_approval_transitions_and_immutability()
    {
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var managerA = Guid.NewGuid();
        var staffA = Guid.NewGuid();
        var activeUser = managerA;
        _currentUser.SetupGet(service => service.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(service => service.UserId).Returns(() => activeUser);

        var foreign = Voucher(areaB, staffA, HarvestStatus.PendingApproval);
        var selfApproval = Voucher(areaA, managerA, HarvestStatus.PendingApproval);
        var workflow = Voucher(areaA, staffA, HarvestStatus.PendingApproval);
        var rejected = Voucher(areaA, staffA, HarvestStatus.PendingApproval);
        var vouchers = new List<HarvestVoucher> { foreign, selfApproval, workflow, rejected };
        var lines = new List<HarvestLine>();
        var crab = new Crab { Id = Guid.NewGuid(), Status = CrabStatus.Alive, Condition = CrabCondition.Normal };
        var saves = 0;

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => vouchers.SingleOrDefault(item => item.Id == id));
        voucherRepo.Setup(repo => repo.Update(It.IsAny<HarvestVoucher>()));
        voucherRepo.Setup(repo => repo.Remove(It.IsAny<HarvestVoucher>()))
            .Callback<HarvestVoucher>(item => vouchers.Remove(item));
        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(repo => repo.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<HarvestLine, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<HarvestLine, bool>> predicate,
                CancellationToken _) => lines.Where(predicate.Compile()).ToList());
        lineRepo.Setup(repo => repo.Remove(It.IsAny<HarvestLine>()))
            .Callback<HarvestLine>(item => lines.Remove(item));

        var users = new Dictionary<Guid, AppUser>
        {
            [managerA] = new()
            {
                Id = managerA, Username = "manager-a", FullName = "Manager A",
                Role = UserRole.FarmManager, IsActive = true
            },
            [staffA] = new()
            {
                Id = staffA, Username = "staff-a", FullName = "Staff A",
                Role = UserRole.FarmStaff, IsActive = true
            }
        };
        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => users.GetValueOrDefault(id));
        var managerAssignments = new Mock<IRepository<FarmManagerAssignment>>();
        managerAssignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmManagerAssignment { ManagerUserId = managerA, FarmingAreaId = areaA });
        var staffAssignments = new Mock<IRepository<FarmStaffAssignment>>();
        staffAssignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = staffA, ManagerUserId = managerA, FarmingAreaId = areaA
            });
        var areas = new Mock<IRepository<FarmingArea>>();
        areas.Setup(repo => repo.GetByIdAsync(areaA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingArea { Id = areaA, Code = "AREA-A", Name = "Khu A" });
        var frozenLots = new Mock<IRepository<FrozenLot>>();
        frozenLots.Setup(repo => repo.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FrozenLot, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _uow.Setup(unit => unit.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(unit => unit.HarvestLines).Returns(lineRepo.Object);
        _uow.Setup(unit => unit.Users).Returns(userRepo.Object);
        _uow.Setup(unit => unit.FarmManagerAssignments).Returns(managerAssignments.Object);
        _uow.Setup(unit => unit.FarmStaffAssignments).Returns(staffAssignments.Object);
        _uow.Setup(unit => unit.FarmingAreas).Returns(areas.Object);
        _uow.Setup(unit => unit.FrozenLots).Returns(frozenLots.Object);
        _uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saves++)
            .ReturnsAsync(1);
        var service = new HarvestService(_uow.Object, _currentUser.Object);

        var foreignAct = () => service.ApproveAsync(foreign.Id, new ApproveHarvestRequest());
        (await foreignAct.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        foreign.Status.Should().Be(HarvestStatus.PendingApproval);
        saves.Should().Be(0);

        var selfAct = () => service.ApproveAsync(selfApproval.Id, new ApproveHarvestRequest());
        (await selfAct.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        selfApproval.Status.Should().Be(HarvestStatus.PendingApproval);
        saves.Should().Be(0);

        activeUser = staffA;
        var earlyExecute = () => service.ExecuteAsync(
            workflow.Id,
            new ExecuteHarvestRequest([new HarvestLineRequest(null, 250, "M", true)]));
        (await earlyExecute.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        workflow.Status.Should().Be(HarvestStatus.PendingApproval);
        saves.Should().Be(0);

        activeUser = managerA;
        await service.ApproveAsync(workflow.Id, new ApproveHarvestRequest());
        workflow.Status.Should().Be(HarvestStatus.Approved);
        var savesAfterApproval = saves;
        var approveTwice = () => service.ApproveAsync(workflow.Id, new ApproveHarvestRequest());
        (await approveTwice.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        saves.Should().Be(savesAfterApproval);

        activeUser = staffA;
        await service.ExecuteAsync(
            workflow.Id,
            new ExecuteHarvestRequest([new HarvestLineRequest(null, 250, "M", true)]));
        workflow.Status.Should().Be(HarvestStatus.InProgress);

        activeUser = managerA;
        await service.ConfirmAsync(workflow.Id, new ConfirmHarvestRequest());
        workflow.Status.Should().Be(HarvestStatus.Completed);
        var savesAfterCompletion = saves;
        var confirmTwice = () => service.ConfirmAsync(workflow.Id, new ConfirmHarvestRequest());
        (await confirmTwice.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        var deleteCompleted = () => service.DeleteAsync(workflow.Id);
        (await deleteCompleted.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        saves.Should().Be(savesAfterCompletion);
        vouchers.Should().Contain(workflow);

        await service.RejectAsync(rejected.Id, new RejectHarvestRequest("Không đạt yêu cầu"));
        rejected.Status.Should().Be(HarvestStatus.Rejected);
        crab.Status.Should().Be(CrabStatus.Alive);
        crab.Condition.Should().Be(CrabCondition.Normal);
    }

    private static HarvestVoucher Voucher(Guid areaId, Guid createdBy, HarvestStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            VoucherCode = $"HAR-{Guid.NewGuid():N}"[..12],
            FarmingAreaId = areaId,
            CreatedBy = createdBy,
            HarvestDate = DateTime.UtcNow,
            Status = status
        };
}
