using System.Linq.Expressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class CrabLotCloseSnapshotTests
{
    [Fact]
    public async Task Close_empty_lot_succeeds_with_zero_snapshot_and_expected_unstocked_count()
    {
        var stockedAt = DateTime.UtcNow.AddDays(-2);
        var lot = Lot(stockedAt, quantity: 10, deadOnArrival: 2);
        var fixture = Fixture(lot);

        var result = await fixture.Service.CloseLotAsync(
            lot.Id, new CloseCrabLotRequest(ClosedAt: stockedAt.AddDays(1)));

        result.Data!.RegisteredCountAtClose.Should().Be(0);
        result.Data.AliveCountAtClose.Should().Be(0);
        result.Data.UnmoltedCountAtClose.Should().Be(0);
        result.Data.MoltedCountAtClose.Should().Be(0);
        result.Data.HarvestedCountAtClose.Should().Be(0);
        result.Data.DeadCountAtClose.Should().Be(0);
        result.Data.UnstockedCountAtClose.Should().Be(8);
        lot.CloseDisposition.Should().BeNull();
        fixture.Uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Living_unmolted_requires_disposition_but_dead_unmolted_does_not_count()
    {
        var stockedAt = DateTime.UtcNow.AddDays(-2);
        var lot = Lot(stockedAt, quantity: 6);
        var aliveWithMoltingHistory = Crab(lot.Id, CrabStatus.Alive);
        var crabs = new List<Crab>
        {
            aliveWithMoltingHistory,
            Crab(lot.Id, CrabStatus.Molting),
            Crab(lot.Id, CrabStatus.Quarantined),
            Crab(lot.Id, CrabStatus.Dead),
            Crab(lot.Id, CrabStatus.Harvested, stockedAt.AddHours(2)),
            Crab(lot.Id, CrabStatus.Sold, stockedAt.AddHours(3))
        };
        var fixture = Fixture(lot, crabs,
        [
            new MoltingRecord
            {
                CrabId = aliveWithMoltingHistory.Id,
                Result = "success",
                MoltTime = stockedAt.AddHours(4)
            }
        ]);

        var rejected = () => fixture.Service.CloseLotAsync(
            lot.Id, new CloseCrabLotRequest(ClosedAt: stockedAt.AddDays(1)));

        var error = await rejected.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(400);
        lot.ClosedAt.Should().BeNull();
        lot.RegisteredCountAtClose.Should().BeNull();
        fixture.Uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var result = await fixture.Service.CloseLotAsync(
            lot.Id,
            new CloseCrabLotRequest(
                "Theo dõi hai con còn sống chưa lột",
                stockedAt.AddDays(1)));

        result.Data!.RegisteredCountAtClose.Should().Be(6);
        result.Data.AliveCountAtClose.Should().Be(3);
        result.Data.UnmoltedCountAtClose.Should().Be(2);
        result.Data.MoltedCountAtClose.Should().Be(3);
        result.Data.HarvestedCountAtClose.Should().Be(2);
        result.Data.DeadCountAtClose.Should().Be(1);
        result.Data.UnstockedCountAtClose.Should().Be(0);
        crabs.Single(crab => crab.Status == CrabStatus.Dead).Status.Should().Be(CrabStatus.Dead);
    }

    [Fact]
    public async Task Close_before_stocked_date_and_second_close_are_rejected_without_changes()
    {
        var stockedAt = DateTime.UtcNow.AddDays(-2);
        var missingStockedAtLot = Lot(stockedAt, quantity: 1);
        missingStockedAtLot.StockedAt = null;
        var missingStockedAtFixture = Fixture(missingStockedAtLot);

        var missingStart = () => missingStockedAtFixture.Service.CloseLotAsync(
            missingStockedAtLot.Id, new CloseCrabLotRequest());

        var missingStartError = await missingStart.Should().ThrowAsync<AppException>();
        missingStartError.Which.StatusCode.Should().Be(400);
        missingStockedAtLot.ClosedAt.Should().BeNull();
        missingStockedAtFixture.Uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var lot = Lot(stockedAt, quantity: 1);
        var fixture = Fixture(lot);

        var invalidDate = () => fixture.Service.CloseLotAsync(
            lot.Id, new CloseCrabLotRequest(ClosedAt: stockedAt.AddTicks(-1)));

        var dateError = await invalidDate.Should().ThrowAsync<AppException>();
        dateError.Which.StatusCode.Should().Be(400);
        lot.ClosedAt.Should().BeNull();
        fixture.Uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var closedAt = stockedAt.AddDays(1);
        await fixture.Service.CloseLotAsync(
            lot.Id, new CloseCrabLotRequest(ClosedAt: closedAt));
        var savesAfterClose = fixture.SaveCount;

        var secondClose = () => fixture.Service.CloseLotAsync(
            lot.Id, new CloseCrabLotRequest(ClosedAt: closedAt.AddDays(1)));

        var closeError = await secondClose.Should().ThrowAsync<AppException>();
        closeError.Which.StatusCode.Should().Be(409);
        lot.ClosedAt.Should().Be(closedAt);
        fixture.SaveCount.Should().Be(savesAfterClose);
    }

    [Fact]
    public async Task Close_snapshot_and_closed_at_do_not_change_with_later_crab_updates()
    {
        var stockedAt = DateTime.UtcNow.AddDays(-2);
        var closedAt = stockedAt.AddDays(1);
        var lot = Lot(stockedAt, quantity: 3);
        var living = Crab(lot.Id, CrabStatus.Alive, stockedAt.AddHours(1));
        var dead = Crab(lot.Id, CrabStatus.Dead);
        var crabs = new List<Crab> { living, dead };
        var fixture = Fixture(lot, crabs);

        await fixture.Service.CloseLotAsync(
            lot.Id, new CloseCrabLotRequest(ClosedAt: closedAt));
        living.Status = CrabStatus.Dead;
        living.MoltedAt = null;
        crabs.Add(Crab(lot.Id, CrabStatus.Alive));

        var read = await fixture.Service.GetLotByIdAsync(lot.Id);

        read.Data!.ClosedAt.Should().Be(closedAt);
        read.Data.PlacedCount.Should().Be(2);
        read.Data.RegisteredCountAtClose.Should().Be(2);
        read.Data.AliveCountAtClose.Should().Be(1);
        read.Data.UnmoltedCountAtClose.Should().Be(0);
        read.Data.MoltedCountAtClose.Should().Be(1);
        read.Data.DeadCountAtClose.Should().Be(1);
        read.Data.UnstockedCountAtClose.Should().Be(1);

        var report = await new SurvivalRateReportService(fixture.Uow.Object, currentUser: null)
            .GetSurvivalRateReportAsync(
                new SurvivalRateFilterDto(null, null, null, lot.Id));
        report.TotalCrabs.Should().Be(2);
        report.AliveCrabs.Should().Be(1);
        report.DeadCrabs.Should().Be(1);
        report.HarvestedCrabs.Should().Be(0);
    }

    private static CrabLot Lot(DateTime stockedAt, int quantity, int deadOnArrival = 0) =>
        new()
        {
            Id = Guid.NewGuid(),
            LotCode = "LOT-CLOSE",
            Name = "Lô đóng",
            Quantity = quantity,
            DeadOnArrival = deadOnArrival,
            StockedAt = stockedAt,
            ImportDate = stockedAt.AddDays(-1)
        };

    private static Crab Crab(Guid lotId, CrabStatus status, DateTime? moltedAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CrabLotId = lotId,
            Code = Guid.NewGuid().ToString("N"),
            Status = status,
            MoltedAt = moltedAt
        };

    private static CloseFixture Fixture(
        CrabLot lot,
        List<Crab>? crabs = null,
        IReadOnlyList<MoltingRecord>? moltingRecords = null)
    {
        crabs ??= new List<Crab>();
        moltingRecords ??= Array.Empty<MoltingRecord>();
        var uow = new Mock<IUnitOfWork>();
        var lots = new Mock<IRepository<CrabLot>>();
        lots.Setup(repo => repo.GetByIdAsync(lot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lot);
        lots.Setup(repo => repo.Update(It.IsAny<CrabLot>()));
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(repo => repo.FindAsync(
                It.IsAny<Expression<Func<Crab, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Crab, bool>> predicate, CancellationToken _) =>
                crabs.Where(predicate.Compile()).ToList());
        crabRepo.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => crabs.ToList());
        uow.Setup(unit => unit.CrabLots).Returns(lots.Object);
        uow.Setup(unit => unit.CrabLotAreas).Returns(Repo<CrabLotArea>().Object);
        uow.Setup(unit => unit.Crabs).Returns(crabRepo.Object);
        uow.Setup(unit => unit.MoltingRecords).Returns(Repo(moltingRecords.ToArray()).Object);
        var saves = 0;
        uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saves++)
            .ReturnsAsync(1);
        var service = new FarmLotService(
            uow.Object, Mock.Of<IPublicImageStorage>(), currentUser: null);
        return new CloseFixture(uow, service, () => saves);
    }

    private static Mock<IRepository<T>> Repo<T>(params T[] items)
        where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        return repository;
    }

    private sealed record CloseFixture(
        Mock<IUnitOfWork> Uow,
        FarmLotService Service,
        Func<int> Saves)
    {
        public int SaveCount => Saves();
    }
}
