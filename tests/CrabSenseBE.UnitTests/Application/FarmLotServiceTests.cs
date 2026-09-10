using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public class FarmLotServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private FarmLotService Create() => new(_uow.Object);

    [Fact]
    public async Task CreateLot_StoresDeclaredQuantity_AndComputesAvgAndCost()
    {
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabLot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        lotRepo.Setup(r => r.AddAsync(It.IsAny<CrabLot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lotRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CrabLot>());

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Crab>());

        _uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CreateLotAsync(new CreateCrabLotRequest(
            Name: "Lô cua tháng 9",
            ImportDate: DateTime.UtcNow,
            Quantity: 100,
            LotCode: "LOT-20260905-001",
            SupplierName: "Nhà cung cấp A",
            TotalWeightKg: 12.5m,
            UnitPriceVndPerKg: 180000,
            ShippingCostVnd: 100000,
            OtherCostVnd: 0));

        result.Success.Should().BeTrue();
        result.Data!.LotCode.Should().Be("LOT-20260905-001");
        result.Data.Name.Should().Be("Lô cua tháng 9");
        result.Data.Quantity.Should().Be(100);
        result.Data.PlacedCount.Should().Be(0);
        result.Data.Status.Should().Be("Pending");
        result.Data.AverageWeightGram.Should().Be(125m);
        result.Data.CrabCostVnd.Should().Be(2_250_000m);
        result.Data.TotalCostVnd.Should().Be(2_350_000m);
    }

    [Fact]
    public async Task GetLots_KeepsDeclaredQuantity_AndCountsPlacedCrabs()
    {
        var lotId = Guid.NewGuid();
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CrabLot
                {
                    Id = lotId,
                    LotCode = "L1",
                    Name = "Lô 1",
                    ImportDate = DateTime.UtcNow,
                    Quantity = 100
                }
            });

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Crab { CrabLotId = lotId, WeightGram = 100 },
                new Crab { CrabLotId = lotId, WeightGram = 200 },
                new Crab { CrabLotId = lotId, WeightGram = null },
            });

        _uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);

        var result = await Create().GetLotsAsync();
        var lot = result.Data!.Single();
        lot.Quantity.Should().Be(100);
        lot.PlacedCount.Should().Be(3);
        lot.Status.Should().Be("Allocating");
    }

    [Fact]
    public async Task GetLots_CancelledStaysCancelled()
    {
        var lotId = Guid.NewGuid();
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new CrabLot
                {
                    Id = lotId,
                    LotCode = "L2",
                    Name = "Lô hủy",
                    Quantity = 50,
                    Status = "Cancelled"
                }
            });
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Crab>());
        _uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);

        var lot = (await Create().GetLotsAsync()).Data!.Single();
        lot.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task CreateLot_DuplicateCode_ThrowsConflict()
    {
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabLot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Create().CreateLotAsync(new CreateCrabLotRequest(
                Name: "Lô",
                Quantity: 10,
                LotCode: "LOT-001")));
        ex.StatusCode.Should().Be(409);
    }
}
