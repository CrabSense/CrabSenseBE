using System.Linq.Expressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class MissingCrabRecoveryTests
{
    [Fact]
    public async Task Recovery_preserves_identity_lot_and_closed_allocation()
    {
        var f = new Fixture();
        await f.Service.RecoverMissingCrabAsync(f.Crab.Id, new(f.Box.Id, "Verified found alive"));
        Assert.Equal(CrabStatus.Alive, f.Crab.Status);
        Assert.Equal(CrabCondition.Problem, f.Crab.Condition);
        Assert.Equal(f.Box.Id, f.Crab.BoxId);
        Assert.Equal(f.Lot.Id, f.Crab.CrabLotId);
        Assert.Equal("CRAB-001", f.Crab.Code);
        Assert.Equal("QR-CRAB-001", f.Crab.QrCode);
        Assert.True(f.Box.IsOccupied);
        Assert.NotNull(f.Previous.EndTime);
        Assert.Equal(2, f.Allocations.Count);
        Assert.Null(f.Allocations[1].EndTime);
        Assert.Equal(CrabStatus.Missing, f.History!.OldStatus);
        Assert.Equal(CrabStatus.Alive, f.History.NewStatus);
        Assert.Equal(f.Manager.Id, f.History.ChangedByUserId);
        f.Uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("closed")]
    [InlineData("dead")]
    [InlineData("reason")]
    [InlineData("otherArea")]
    public async Task Invalid_recovery_does_not_save(string scenario)
    {
        var f = new Fixture();
        if (scenario == "occupied") f.Box.IsOccupied = true;
        if (scenario == "closed") f.Lot.ClosedAt = DateTime.UtcNow;
        if (scenario == "dead") f.Crab.Status = CrabStatus.Dead;
        if (scenario == "otherArea") f.TargetRow.FarmingAreaId = Guid.NewGuid();
        await Assert.ThrowsAsync<AppException>(() => f.Service.RecoverMissingCrabAsync(
            f.Crab.Id, new(f.Box.Id, scenario == "reason" ? "" : "Found")));
        Assert.Single(f.Allocations);
        Assert.Null(f.History);
        f.Uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new();
        public AppUser Manager { get; } = new() { Id = Guid.NewGuid(), IsActive = true, Role = UserRole.FarmManager, Username = "manager" };
        public FarmingArea Area { get; } = new() { Id = Guid.NewGuid(), IsActive = true };
        public FarmingRow TargetRow { get; }
        public CrabLot Lot { get; } = new() { Id = Guid.NewGuid(), Status = "Allocating", Quantity = 1 };
        public Crab Crab { get; }
        public Box Box { get; }
        public CrabBoxAllocation Previous { get; }
        public List<CrabBoxAllocation> Allocations { get; }
        public CrabStatusHistory? History { get; private set; }
        public FarmingService Service { get; }

        public Fixture()
        {
            var oldRow = new FarmingRow { Id = Guid.NewGuid(), FarmingAreaId = Area.Id, IsActive = true };
            TargetRow = new FarmingRow { Id = Guid.NewGuid(), FarmingAreaId = Area.Id, IsActive = true };
            var oldBox = new Box { Id = Guid.NewGuid(), FarmingRowId = oldRow.Id, Status = "empty" };
            Box = new Box { Id = Guid.NewGuid(), FarmingRowId = TargetRow.Id, Status = "empty" };
            Crab = new Crab { Id = Guid.NewGuid(), CrabLotId = Lot.Id, Status = CrabStatus.Missing,
                Condition = CrabCondition.Problem, Code = "CRAB-001", QrCode = "QR-CRAB-001" };
            Previous = new CrabBoxAllocation { Id = Guid.NewGuid(), CrabId = Crab.Id, BoxId = oldBox.Id,
                StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-1) };
            Allocations = [Previous];
            var boxes = Repo(new List<Box> { oldBox, Box }, x => x.Id);
            var rows = Repo(new List<FarmingRow> { oldRow, TargetRow }, x => x.Id);
            var allocations = Repo(Allocations, x => x.Id);
            allocations.Setup(x => x.AddAsync(It.IsAny<CrabBoxAllocation>(), It.IsAny<CancellationToken>()))
                .Callback<CrabBoxAllocation, CancellationToken>((a, _) => Allocations.Add(a)).Returns(Task.CompletedTask);
            var history = Repo(new List<CrabStatusHistory>(), x => x.Id);
            history.Setup(x => x.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
                .Callback<CrabStatusHistory, CancellationToken>((h, _) => History = h).Returns(Task.CompletedTask);
            Uow.Setup(x => x.GetCrabWithDetailsAsync(Crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Crab);
            Uow.Setup(x => x.Crabs).Returns(Repo(new List<Crab> { Crab }, x => x.Id).Object);
            Uow.Setup(x => x.Boxes).Returns(boxes.Object);
            Uow.Setup(x => x.FarmingRows).Returns(rows.Object);
            Uow.Setup(x => x.FarmingAreas).Returns(Repo(new List<FarmingArea> { Area }, x => x.Id).Object);
            Uow.Setup(x => x.CrabLots).Returns(Repo(new List<CrabLot> { Lot }, x => x.Id).Object);
            Uow.Setup(x => x.CrabLotAreas).Returns(Repo(new List<CrabLotArea> { new() { CrabLotId = Lot.Id, FarmingAreaId = Area.Id } }, _ => Guid.Empty).Object);
            Uow.Setup(x => x.CrabBoxAllocations).Returns(allocations.Object);
            Uow.Setup(x => x.CrabStatusHistories).Returns(history.Object);
            Uow.Setup(x => x.BoxStatusHistories).Returns(Repo(new List<BoxStatusHistory>(), x => x.Id).Object);
            Uow.Setup(x => x.CrabHarvestHistories).Returns(Repo(new List<CrabHarvestHistory>(), x => x.Id).Object);
            Uow.Setup(x => x.CrabMortalityRecords).Returns(Repo(new List<CrabMortalityRecord>(), x => x.Id).Object);
            Uow.Setup(x => x.HarvestAssignmentLines).Returns(Repo(new List<HarvestAssignmentLine>(), x => x.Id).Object);
            Uow.Setup(x => x.HarvestLines).Returns(Repo(new List<HarvestLine>(), x => x.Id).Object);
            Uow.Setup(x => x.Users).Returns(Repo(new List<AppUser> { Manager }, x => x.Id).Object);
            Uow.Setup(x => x.FarmManagerAssignments).Returns(Repo(new List<FarmManagerAssignment> { new() { ManagerUserId = Manager.Id, FarmingAreaId = Area.Id } }, _ => Guid.Empty).Object);
            Uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            var user = new Mock<ICurrentUserService>();
            user.SetupGet(x => x.UserId).Returns(Manager.Id);
            user.SetupGet(x => x.IsAuthenticated).Returns(true);
            Service = new FarmingService(Uow.Object, Mock.Of<IBoxQrService>(), Mock.Of<IPublicImageStorage>(), user.Object);
        }
    }

    private static Mock<IRepository<T>> Repo<T>(List<T> items, Func<T, Guid> id) where T : class
    {
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, CancellationToken _) => items.FirstOrDefault(x => id(x) == key));
        repo.Setup(x => x.FindAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> p, CancellationToken _) => items.Where(p.Compile()).ToList());
        repo.Setup(x => x.AnyAsync(It.IsAny<Expression<Func<T, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> p, CancellationToken _) => items.Any(p.Compile()));
        repo.Setup(x => x.AddAsync(It.IsAny<T>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return repo;
    }
}
