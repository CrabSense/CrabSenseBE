using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class CrabLotComparisonReportServiceTests
{
    [Fact]
    public async Task Manager_view_uses_history_and_latest_allocation_without_leaking_other_area()
    {
        var fixture = Fixture(UserRole.FarmManager);

        var rows = await fixture.Service.CompareAsync(null);

        var row = rows.Should().ContainSingle().Subject;
        row.LotCode.Should().Be("LOT-AB");
        row.FarmingAreaIds.Should().Equal(fixture.AreaA);
        row.IsPartialAreaView.Should().BeTrue();
        row.DeclaredQuantity.Should().BeNull();
        row.CrabCount.Should().Be(3);
        row.AliveCount.Should().Be(1);
        row.MoltedEventCount.Should().Be(1);
        row.UnmoltedCount.Should().Be(0);
        row.CurrentHarvestedStatusCount.Should().Be(1);
        row.HarvestedCount.Should().Be(1);
        row.HarvestedWeightKg.Should().Be(0.25m);
        row.UnresolvedAreaCount.Should().BeNull(
            "partial views must not reveal unresolved or other-area totals");
        row.RegisteredCountAtClose.Should().BeNull();
        row.EndedAt.Should().Be(fixture.ClosedAt);
        row.EndedAt.Should().NotBe(fixture.UpdatedAt);
    }

    [Fact]
    public async Task Administrator_with_all_lot_areas_gets_one_deduplicated_row_and_close_snapshot()
    {
        var fixture = Fixture(UserRole.FarmAdministrator);

        var rows = await fixture.Service.CompareAsync(null);

        var row = rows.Single(item => item.LotCode == "LOT-AB");
        row.FarmingAreaIds.Should().BeEquivalentTo([fixture.AreaA, fixture.AreaB]);
        row.IsPartialAreaView.Should().BeFalse();
        row.DeclaredQuantity.Should().Be(10);
        row.CrabCount.Should().Be(5,
            "the unresolved harvested crab is marked separately, not guessed into an area");
        row.HarvestedCount.Should().Be(2);
        row.HarvestedWeightKg.Should().Be(0.55m);
        row.UnresolvedAreaCount.Should().Be(1);
        row.RegisteredCountAtClose.Should().Be(6);
        row.AliveCountAtClose.Should().Be(2);
        row.HarvestedCountAtClose.Should().Be(3);
        rows.Count(item => item.CrabLotId == row.CrabLotId).Should().Be(1);
    }

    private static FixtureData Fixture(UserRole role)
    {
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowA = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        var boxA = Guid.NewGuid();
        var boxB = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var lotBId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var closedAt = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new AppUser
        {
            Id = userId,
            Username = "report-user",
            Role = role,
            IsActive = true
        };
        var areas = new List<FarmingArea>
        {
            new()
            {
                Id = areaA, Code = "AREA-A", Name = "Khu A",
                Administrators = role == UserRole.FarmAdministrator ? [user] : []
            },
            new()
            {
                Id = areaB, Code = "AREA-B", Name = "Khu B",
                Administrators = role == UserRole.FarmAdministrator ? [user] : []
            }
        };
        var lots = new List<CrabLot>
        {
            new()
            {
                Id = lotId,
                LotCode = "LOT-AB",
                Name = "Lô hai khu",
                Quantity = 10,
                StockedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ClosedAt = closedAt,
                UpdatedAt = updatedAt,
                RegisteredCountAtClose = 6,
                AliveCountAtClose = 2,
                UnmoltedCountAtClose = 1,
                MoltedCountAtClose = 2,
                HarvestedCountAtClose = 3,
                DeadCountAtClose = 1,
                UnstockedCountAtClose = 4
            },
            new()
            {
                Id = lotBId,
                LotCode = "LOT-B",
                Name = "Chỉ khu B",
                Quantity = 0
            }
        };
        var currentA = Crab(lotId, boxA, CrabStatus.Alive, "CURRENT-A");
        var harvestedA = Crab(lotId, null, CrabStatus.Harvested, "HARVEST-A");
        var deadA = Crab(lotId, null, CrabStatus.Dead, "DEAD-A");
        var movedToB = Crab(lotId, null, CrabStatus.Alive, "MOVED-B");
        var harvestedB = Crab(lotId, null, CrabStatus.Harvested, "HARVEST-B");
        var unresolved = Crab(lotId, null, CrabStatus.Harvested, "UNKNOWN");
        var crabs = new List<Crab>
        {
            currentA, harvestedA, deadA, movedToB, harvestedB, unresolved
        };
        var voucherA = Voucher(areaA, "HV-A");
        var voucherB = Voucher(areaB, "HV-B");
        var lineA = Line(voucherA.Id, harvestedA.Id, boxA, 250);
        var lineB = Line(voucherB.Id, harvestedB.Id, boxB, 300);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(ListRepo([user]).Object);
        var areaRepo = ListRepo(areas);
        areaRepo.Setup(repo => repo.Query()).Returns(() => areas.AsQueryable());
        uow.Setup(unit => unit.FarmingAreas).Returns(areaRepo.Object);
        uow.Setup(unit => unit.FarmManagerAssignments).Returns(
            ListRepo(new[]
            {
                new FarmManagerAssignment
                {
                    ManagerUserId = userId,
                    FarmingAreaId = areaA
                }
            }).Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(
            ListRepo(Array.Empty<FarmStaffAssignment>()).Object);
        uow.Setup(unit => unit.CrabLots).Returns(ListRepo(lots).Object);
        uow.Setup(unit => unit.CrabLotAreas).Returns(ListRepo(new[]
        {
            new CrabLotArea { CrabLotId = lotId, FarmingAreaId = areaA },
            new CrabLotArea { CrabLotId = lotId, FarmingAreaId = areaB },
            new CrabLotArea { CrabLotId = lotBId, FarmingAreaId = areaB }
        }).Object);
        uow.Setup(unit => unit.Crabs).Returns(ListRepo(crabs).Object);
        uow.Setup(unit => unit.FarmingRows).Returns(ListRepo(new[]
        {
            new FarmingRow { Id = rowA, FarmingAreaId = areaA, Code = "ROW-A" },
            new FarmingRow { Id = rowB, FarmingAreaId = areaB, Code = "ROW-B" }
        }).Object);
        uow.Setup(unit => unit.Boxes).Returns(ListRepo(new[]
        {
            new Box { Id = boxA, FarmingRowId = rowA, Code = "BOX-A" },
            new Box { Id = boxB, FarmingRowId = rowB, Code = "BOX-B" }
        }).Object);
        uow.Setup(unit => unit.CrabBoxAllocations).Returns(ListRepo(new[]
        {
            Allocation(deadA.Id, boxA, 1),
            Allocation(movedToB.Id, boxA, 1),
            Allocation(movedToB.Id, boxB, 2)
        }).Object);
        uow.Setup(unit => unit.HarvestVouchers).Returns(
            ListRepo(new[] { voucherA, voucherB }).Object);
        uow.Setup(unit => unit.HarvestLines).Returns(
            ListRepo(new[] { lineA, lineB }).Object);
        uow.Setup(unit => unit.HarvestApprovedLines).Returns(ListRepo(new[]
        {
            Approved(voucherA, lineA, harvestedA.Id, areaA),
            Approved(voucherB, lineB, harvestedB.Id, areaB)
        }).Object);
        uow.Setup(unit => unit.CrabHarvestHistories).Returns(ListRepo(new[]
        {
            History(harvestedA.Id, lineA.Id, 250),
            History(harvestedA.Id, lineA.Id, 250),
            History(harvestedB.Id, lineB.Id, 300)
        }).Object);
        uow.Setup(unit => unit.MoltingRecords).Returns(ListRepo(new[]
        {
            new MoltingRecord
            {
                CrabId = currentA.Id,
                BoxId = boxA,
                Result = "success",
                IsVoided = false
            }
        }).Object);
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(userId);
        return new FixtureData(
            new CrabLotComparisonReportService(uow.Object, current.Object),
            areaA,
            areaB,
            closedAt,
            updatedAt);
    }

    private static Crab Crab(
        Guid lotId,
        Guid? boxId,
        CrabStatus status,
        string code) =>
        new()
        {
            Id = Guid.NewGuid(),
            CrabLotId = lotId,
            BoxId = boxId,
            Status = status,
            Code = code
        };

    private static HarvestVoucher Voucher(Guid areaId, string code) =>
        new()
        {
            Id = Guid.NewGuid(),
            VoucherCode = code,
            FarmingAreaId = areaId,
            Status = HarvestStatus.Completed,
            ApprovalVersion = 1,
            HarvestDate = DateTime.UtcNow,
            ConfirmedAt = DateTime.UtcNow
        };

    private static HarvestLine Line(
        Guid voucherId,
        Guid crabId,
        Guid boxId,
        decimal weight) =>
        new()
        {
            Id = Guid.NewGuid(),
            HarvestVoucherId = voucherId,
            CrabId = crabId,
            BoxId = boxId,
            WeightGram = weight,
            Result = "passed"
        };

    private static HarvestApprovedLine Approved(
        HarvestVoucher voucher,
        HarvestLine line,
        Guid crabId,
        Guid areaId) =>
        new()
        {
            HarvestVoucherId = voucher.Id,
            ApprovalVersion = voucher.ApprovalVersion,
            SourceHarvestLineId = line.Id,
            CrabId = crabId,
            BoxId = line.BoxId,
            FarmingAreaId = areaId
        };

    private static CrabHarvestHistory History(
        Guid crabId,
        Guid lineId,
        decimal weight) =>
        new()
        {
            CrabId = crabId,
            HarvestLineId = lineId,
            WeightGram = weight,
            HarvestedAt = DateTime.UtcNow
        };

    private static CrabBoxAllocation Allocation(
        Guid crabId,
        Guid boxId,
        int day) =>
        new()
        {
            CrabId = crabId,
            BoxId = boxId,
            StartTime = new DateTime(2026, 9, day, 0, 0, 0, DateTimeKind.Utc)
        };

    private static Mock<IRepository<T>> ListRepo<T>(IEnumerable<T> source)
        where T : class
    {
        var items = source.ToList();
        var repo = new Mock<IRepository<T>>();
        repo.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        repo.Setup(item => item.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<T, bool>> predicate,
                CancellationToken _) => items.Where(predicate.Compile()).ToList());
        repo.Setup(item => item.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                items.OfType<CrabSenseBE.Domain.Common.BaseEntity>()
                    .FirstOrDefault(item => item.Id == id) as T);
        repo.Setup(item => item.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<T, bool>> predicate,
                CancellationToken _) => items.FirstOrDefault(predicate.Compile()));
        return repo;
    }

    private sealed record FixtureData(
        CrabLotComparisonReportService Service,
        Guid AreaA,
        Guid AreaB,
        DateTime ClosedAt,
        DateTime UpdatedAt);
}
