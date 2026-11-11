using System.Linq.Expressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class HarvestApprovalSnapshotTests
{
    [Fact]
    public void Voucher_version_is_an_optimistic_concurrency_token()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new AppDbContext(options);

        context.Model.FindEntityType(typeof(HarvestVoucher))!
            .FindProperty(nameof(HarvestVoucher.ConcurrencyVersion))!
            .IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public async Task Staff_executes_only_approved_crabs_and_manager_confirmation_preserves_snapshot()
    {
        var fixture = Fixture();
        var snapshotIds = fixture.ApprovedLines.Select(line => line.Id).ToArray();

        var executed = await fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest(
            [
                Result(fixture.CrabA.Id, 250),
                Result(fixture.CrabB.Id, 270)
            ]));

        executed.Data!.ExecutionShortfallCount.Should().Be(0);
        fixture.Voucher.Status.Should().Be(HarvestStatus.InProgress);
        fixture.CrabA.Status.Should().Be(CrabStatus.Alive,
            "physical execution is not final until manager confirmation");
        fixture.ActiveUser = fixture.ManagerId;

        await fixture.Service.ConfirmAsync(
            fixture.Voucher.Id, new ConfirmHarvestRequest("Đã đối chiếu"));

        fixture.Voucher.Status.Should().Be(HarvestStatus.Completed);
        fixture.CrabA.Status.Should().Be(CrabStatus.Harvested);
        fixture.CrabB.Status.Should().Be(CrabStatus.Harvested);
        fixture.ApprovedLines.Select(line => line.Id).Should().Equal(snapshotIds);
        fixture.ApprovedLines.Should().OnlyContain(line => line.ApprovalVersion == 1);
    }

    [Fact]
    public async Task Extra_crab_is_rejected_before_any_harvest_state_changes()
    {
        var fixture = Fixture();
        var saves = fixture.SaveCount;

        var act = () => fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest(
            [
                Result(fixture.CrabA.Id, 250),
                Result(fixture.CrabC.Id, 260)
            ]));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        fixture.Voucher.Status.Should().Be(HarvestStatus.Approved);
        fixture.CrabA.Status.Should().Be(CrabStatus.Alive);
        fixture.CrabC.Status.Should().Be(CrabStatus.Alive);
        fixture.SaveCount.Should().Be(saves);
        fixture.ActualLines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Shortfall_requires_reason_and_records_actual_subset()
    {
        var fixture = Fixture();
        var saves = fixture.SaveCount;
        var missingReason = () => fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest([Result(fixture.CrabA.Id, 250)]));

        var error = await missingReason.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(400);
        fixture.SaveCount.Should().Be(saves);
        fixture.Voucher.Status.Should().Be(HarvestStatus.Approved);

        var result = await fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest(
                [Result(fixture.CrabA.Id, 250)],
                ShortfallReason: "Không tìm thấy cua B tại hộp"));

        result.Data!.ExecutionShortfallCount.Should().Be(1);
        result.Data.ExecutionShortfallReason.Should().Be("Không tìm thấy cua B tại hộp");
        fixture.ActualLines.Should().ContainSingle(line => line.CrabId == fixture.CrabA.Id);
        fixture.ApprovedLines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Adjustment_keeps_old_snapshot_and_requires_manager_reapproval()
    {
        var fixture = Fixture();

        await fixture.Service.RequestAdjustmentAsync(
            fixture.Voucher.Id,
            new RequestHarvestAdjustmentRequest(
            [
                Result(fixture.CrabA.Id, 0),
                Result(fixture.CrabC.Id, 0)
            ],
            "Thay cua B bằng cua C"));

        fixture.Voucher.Status.Should().Be(HarvestStatus.PendingApproval);
        fixture.ApprovedLines.Should().HaveCount(2);
        fixture.Voucher.ApprovalVersion.Should().Be(1);
        var execute = () => fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest([Result(fixture.CrabA.Id, 250)]));
        (await execute.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);

        fixture.ActiveUser = fixture.ManagerId;
        await fixture.Service.ApproveAsync(
            fixture.Voucher.Id, new ApproveHarvestRequest("Duyệt điều chỉnh"));

        fixture.Voucher.ApprovalVersion.Should().Be(2);
        fixture.ApprovedLines.Count(line => line.ApprovalVersion == 1).Should().Be(2);
        fixture.ApprovedLines.Count(line => line.ApprovalVersion == 2).Should().Be(2);
    }

    [Fact]
    public async Task Crab_already_recorded_by_another_voucher_cannot_be_executed()
    {
        var fixture = Fixture();
        var otherVoucher = new HarvestVoucher
        {
            Id = Guid.NewGuid(),
            FarmingAreaId = fixture.Voucher.FarmingAreaId,
            Status = HarvestStatus.Approved
        };
        await fixture.Uow.Object.HarvestVouchers.AddAsync(otherVoucher);
        fixture.ActualLines.Add(new HarvestLine
        {
            HarvestVoucherId = otherVoucher.Id,
            CrabId = fixture.CrabA.Id,
            WeightGram = 200
        });
        var saves = fixture.SaveCount;

        var act = () => fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest(
            [
                Result(fixture.CrabA.Id, 250),
                Result(fixture.CrabB.Id, 270)
            ]));

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        fixture.SaveCount.Should().Be(saves);
        fixture.CrabA.Status.Should().Be(CrabStatus.Alive);
    }

    [Fact]
    public async Task Rejected_voucher_releases_crab_for_a_new_approved_voucher()
    {
        var fixture = Fixture();
        var rejectedVoucher = new HarvestVoucher
        {
            Id = Guid.NewGuid(),
            FarmingAreaId = fixture.Voucher.FarmingAreaId,
            Status = HarvestStatus.Rejected
        };
        await fixture.Uow.Object.HarvestVouchers.AddAsync(rejectedVoucher);
        fixture.ActualLines.Add(new HarvestLine
        {
            HarvestVoucherId = rejectedVoucher.Id,
            CrabId = fixture.CrabA.Id,
            WeightGram = 0
        });

        await fixture.Service.ExecuteAsync(
            fixture.Voucher.Id,
            new ExecuteHarvestRequest(
            [
                Result(fixture.CrabA.Id, 250),
                Result(fixture.CrabB.Id, 270)
            ]));

        fixture.Voucher.Status.Should().Be(HarvestStatus.InProgress);
        fixture.CrabA.Status.Should().Be(
            CrabStatus.Alive,
            "physical execution remains pending until manager confirmation");
    }

    private static HarvestLineRequest Result(Guid crabId, decimal weight) =>
        new(crabId, weight, "M", true);

    private static HarvestFixture Fixture()
    {
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var activeUser = staffId;
        var voucher = new HarvestVoucher
        {
            Id = Guid.NewGuid(),
            VoucherCode = "HAR-SNAPSHOT",
            FarmingAreaId = areaId,
            CreatedBy = Guid.NewGuid(),
            HarvestDate = DateTime.UtcNow,
            Status = HarvestStatus.Approved,
            ApprovalVersion = 1,
            ApprovedQuantity = 2,
            ApprovedByUserId = managerId,
            ApprovedAt = DateTime.UtcNow
        };
        var crabA = Crab(voucher.Id, boxId, "CRAB-A");
        var crabB = Crab(voucher.Id, boxId, "CRAB-B");
        var crabC = Crab(voucher.Id, boxId, "CRAB-C");
        var crabs = new List<Crab> { crabA, crabB, crabC };
        var actualLines = new List<HarvestLine>
        {
            Planned(voucher.Id, crabA, boxId),
            Planned(voucher.Id, crabB, boxId)
        };
        var approvedLines = new List<HarvestApprovedLine>
        {
            Approved(voucher, crabA, boxId, areaId, managerId),
            Approved(voucher, crabB, boxId, areaId, managerId)
        };
        var uow = new Mock<IUnitOfWork>();
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(() => activeUser);
        var users = new List<AppUser>
        {
            new() { Id = managerId, Username = "fm", FullName = "FM", Role = UserRole.FarmManager, IsActive = true },
            new() { Id = staffId, Username = "fs", FullName = "FS", Role = UserRole.FarmStaff, IsActive = true }
        };
        uow.Setup(unit => unit.Users).Returns(Repo(
            users, user => user.Id).Object);
        var staffAssignments = new Mock<IRepository<FarmStaffAssignment>>();
        staffAssignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = staffId,
                ManagerUserId = managerId,
                FarmingAreaId = areaId
            });
        var managerAssignments = new Mock<IRepository<FarmManagerAssignment>>();
        managerAssignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmManagerAssignment
            {
                ManagerUserId = managerId,
                FarmingAreaId = areaId
            });
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(staffAssignments.Object);
        uow.Setup(unit => unit.FarmManagerAssignments).Returns(managerAssignments.Object);
        uow.Setup(unit => unit.HarvestVouchers).Returns(
            Repo(new List<HarvestVoucher> { voucher }, item => item.Id).Object);
        uow.Setup(unit => unit.HarvestLines).Returns(
            Repo(actualLines, item => item.Id).Object);
        uow.Setup(unit => unit.HarvestApprovedLines).Returns(
            Repo(approvedLines, item => item.Id).Object);
        uow.Setup(unit => unit.Crabs).Returns(
            Repo(crabs, item => item.Id).Object);
        uow.Setup(unit => unit.FarmingAreas).Returns(
            Repo(new List<FarmingArea>
            {
                new() { Id = areaId, Code = "AREA", Name = "Khu A" }
            }, item => item.Id).Object);
        uow.Setup(unit => unit.FarmingRows).Returns(
            Repo(new List<FarmingRow>
            {
                new() { Id = rowId, FarmingAreaId = areaId, Code = "ROW" }
            }, item => item.Id).Object);
        uow.Setup(unit => unit.Boxes).Returns(
            Repo(new List<Box>
            {
                new() { Id = boxId, FarmingRowId = rowId, Code = "BOX", IsOccupied = true }
            }, item => item.Id).Object);
        uow.Setup(unit => unit.CrabLots).Returns(Repo(new List<CrabLot>(), item => item.Id).Object);
        uow.Setup(unit => unit.CrabHarvestHistories).Returns(
            Repo(new List<CrabHarvestHistory>(), item => item.Id).Object);
        uow.Setup(unit => unit.CrabBoxAllocations).Returns(
            Repo(new List<CrabBoxAllocation>(), item => item.Id).Object);
        uow.Setup(unit => unit.CrabStatusHistories).Returns(
            Repo(new List<CrabStatusHistory>(), item => item.Id).Object);
        uow.Setup(unit => unit.BoxStatusHistories).Returns(
            Repo(new List<BoxStatusHistory>(), item => item.Id).Object);
        var saves = 0;
        uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saves++)
            .ReturnsAsync(1);

        var fixture = new HarvestFixture(
            uow,
            new HarvestService(uow.Object, current.Object),
            voucher,
            crabA,
            crabB,
            crabC,
            actualLines,
            approvedLines,
            managerId,
            () => activeUser,
            value => activeUser = value,
            () => saves);
        return fixture;
    }

    private static Crab Crab(Guid lotId, Guid boxId, string code) =>
        new()
        {
            Id = Guid.NewGuid(),
            CrabLotId = lotId,
            BoxId = boxId,
            Code = code,
            Status = CrabStatus.Alive,
            Condition = CrabCondition.Normal,
            StockedAt = DateTime.UtcNow
        };

    private static HarvestLine Planned(Guid voucherId, Crab crab, Guid boxId) =>
        new()
        {
            Id = Guid.NewGuid(),
            HarvestVoucherId = voucherId,
            CrabId = crab.Id,
            BoxId = boxId,
            CrabCode = crab.Code,
            WeightGram = 0
        };

    private static HarvestApprovedLine Approved(
        HarvestVoucher voucher,
        Crab crab,
        Guid boxId,
        Guid areaId,
        Guid managerId) =>
        new()
        {
            Id = Guid.NewGuid(),
            HarvestVoucherId = voucher.Id,
            ApprovalVersion = 1,
            CrabId = crab.Id,
            BoxId = boxId,
            FarmingAreaId = areaId,
            ApprovedByUserId = managerId,
            ApprovedAt = DateTime.UtcNow,
            CrabCode = crab.Code,
            BoxCode = "BOX"
        };

    private static Mock<IRepository<T>> Repo<T>(List<T> items, Func<T, Guid> id)
        where T : BaseEntity
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(item => item.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, CancellationToken _) => items.SingleOrDefault(value => id(value) == key));
        repo.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => items.ToList());
        repo.Setup(item => item.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.Where(predicate.Compile()).ToList());
        repo.Setup(item => item.AnyAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.Any(predicate.Compile()));
        repo.Setup(item => item.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>()))
            .Callback<T, CancellationToken>((value, _) => items.Add(value))
            .Returns(Task.CompletedTask);
        repo.Setup(item => item.Remove(It.IsAny<T>()))
            .Callback<T>(value => items.Remove(value));
        repo.Setup(item => item.Update(It.IsAny<T>()));
        return repo;
    }

    private sealed record HarvestFixture(
        Mock<IUnitOfWork> Uow,
        HarvestService Service,
        HarvestVoucher Voucher,
        Crab CrabA,
        Crab CrabB,
        Crab CrabC,
        List<HarvestLine> ActualLines,
        List<HarvestApprovedLine> ApprovedLines,
        Guid ManagerId,
        Func<Guid> GetActiveUser,
        Action<Guid> SetActiveUser,
        Func<int> Saves)
    {
        public Guid ActiveUser
        {
            get => GetActiveUser();
            set => SetActiveUser(value);
        }

        public int SaveCount => Saves();
    }
}
