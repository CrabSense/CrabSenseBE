using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class ClosedCrabLotRegistrationTests
{
    [Fact]
    public async Task CreateCrab_ClosedLot_IsRejectedBeforeAnyDatabaseMutation()
    {
        var fixture = Fixture(new CrabLot
        {
            Id = Guid.NewGuid(),
            Quantity = 10,
            Status = "Allocating",
            ClosedAt = DateTime.UtcNow
        });

        var act = () => fixture.Service.CreateCrabAsync(
            new CreateCrabRequest(fixture.Lot.Id));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(409);
        fixture.Crabs.Verify(
            repo => repo.AddAsync(It.IsAny<Crab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.UnitOfWork.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateCrabsBulk_ClosedLot_RejectsTheWholeBatch()
    {
        var fixture = Fixture(new CrabLot
        {
            Id = Guid.NewGuid(),
            Quantity = 10,
            Status = "Completed",
            ClosedAt = DateTime.UtcNow
        });
        var request = new CreateCrabsBulkRequest(
            fixture.Lot.Id,
            Items:
            [
                new CreateCrabBulkItem(WeightGram: 200, CarapaceWidthMm: 70, CarapaceLengthMm: 80),
                new CreateCrabBulkItem(WeightGram: 210, CarapaceWidthMm: 72, CarapaceLengthMm: 82)
            ]);

        var act = () => fixture.Service.CreateCrabsBulkAsync(request);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(409);
        fixture.Crabs.Verify(
            repo => repo.AddAsync(It.IsAny<Crab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.UnitOfWork.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateCrab_CancelledLot_IsStillRejectedWithoutMutation()
    {
        var fixture = Fixture(new CrabLot
        {
            Id = Guid.NewGuid(),
            Quantity = 10,
            Status = "Cancelled"
        });

        var act = () => fixture.Service.CreateCrabAsync(
            new CreateCrabRequest(fixture.Lot.Id));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(400);
        fixture.Crabs.Verify(
            repo => repo.AddAsync(It.IsAny<Crab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.UnitOfWork.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void UpdateCrabRequest_DoesNotExposeCrabLotId()
    {
        typeof(UpdateCrabRequest).GetProperty(nameof(Crab.CrabLotId))
            .Should().BeNull();
    }

    [Fact]
    public async Task BulkCreate_WithOneBoxOutsideLotAreas_DoesNotSavePartialData()
    {
        var lot = new CrabLot { Id = Guid.NewGuid(), Quantity = 10, Status = "Allocating" };
        var areaA = new FarmingArea { Id = Guid.NewGuid(), Code = "AREA-A", IsActive = true };
        var areaB = new FarmingArea { Id = Guid.NewGuid(), Code = "AREA-B", IsActive = true };
        var rowA = new FarmingRow
        {
            Id = Guid.NewGuid(), FarmingAreaId = areaA.Id, Code = "ROW-A", IsActive = true
        };
        var rowB = new FarmingRow
        {
            Id = Guid.NewGuid(), FarmingAreaId = areaB.Id, Code = "ROW-B", IsActive = true
        };
        var boxA = new Box { Id = Guid.NewGuid(), FarmingRowId = rowA.Id, Code = "BOX-A" };
        var boxB = new Box { Id = Guid.NewGuid(), FarmingRowId = rowB.Id, Code = "BOX-B" };
        var lots = RepositoryById([lot], item => item.Id);
        var areas = RepositoryById([areaA, areaB], item => item.Id);
        var rows = RepositoryById([rowA, rowB], item => item.Id);
        var boxes = RepositoryById([boxA, boxB], item => item.Id);
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(repo => repo.CountAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var links = new Mock<IRepository<CrabLotArea>>();
        links.Setup(repo => repo.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<CrabLotArea, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<CrabLotArea, bool>> predicate,
                CancellationToken _) => predicate.Compile()(new CrabLotArea
                {
                    CrabLotId = lot.Id,
                    FarmingAreaId = areaA.Id
                }));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.CrabLots).Returns(lots.Object);
        uow.Setup(unit => unit.FarmingAreas).Returns(areas.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.Crabs).Returns(crabs.Object);
        uow.Setup(unit => unit.CrabLotAreas).Returns(links.Object);
        var service = new FarmingService(
            uow.Object, Mock.Of<IBoxQrService>(), Mock.Of<IPublicImageStorage>(), currentUser: null);
        var request = new CreateCrabsBulkRequest(
            lot.Id,
            Items:
            [
                new CreateCrabBulkItem(
                    WeightGram: 200, CarapaceWidthMm: 70, CarapaceLengthMm: 80,
                    AutoAssign: false, TargetBoxId: boxA.Id),
                new CreateCrabBulkItem(
                    WeightGram: 210, CarapaceWidthMm: 72, CarapaceLengthMm: 82,
                    AutoAssign: false, TargetBoxId: boxB.Id)
            ]);

        var act = () => service.CreateCrabsBulkAsync(request);

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        crabs.Verify(
            repo => repo.AddAsync(It.IsAny<Crab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static RegistrationFixture Fixture(CrabLot lot)
    {
        var lots = new Mock<IRepository<CrabLot>>();
        lots.Setup(repo => repo.GetByIdAsync(lot.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lot);
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(repo => repo.CountAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.CrabLots).Returns(lots.Object);
        uow.Setup(unit => unit.Crabs).Returns(crabs.Object);
        var service = new FarmingService(
            uow.Object,
            Mock.Of<IBoxQrService>(),
            Mock.Of<IPublicImageStorage>(),
            currentUser: null);
        return new RegistrationFixture(service, lot, crabs, uow);
    }

    private sealed record RegistrationFixture(
        FarmingService Service,
        CrabLot Lot,
        Mock<IRepository<Crab>> Crabs,
        Mock<IUnitOfWork> UnitOfWork);

    private static Mock<IRepository<T>> RepositoryById<T>(
        IReadOnlyCollection<T> items,
        Func<T, Guid> id)
        where T : class
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(repo => repo.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, CancellationToken _) =>
                items.SingleOrDefault(item => id(item) == key));
        return repository;
    }
}
