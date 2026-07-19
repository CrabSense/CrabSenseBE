using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: tạo lô cua / vụ nuôi.</summary>
public class FarmLotServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private FarmLotService Create() => new(_uow.Object);

    [Fact]
    public async Task CreateLot_StartsWithZeroQuantity_ComputedFromCrabs()
    {
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabLot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        lotRepo.Setup(r => r.AddAsync(It.IsAny<CrabLot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Crab>());

        _uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CreateLotAsync(new CreateCrabLotRequest(
            "LOT-001", DateTime.UtcNow, "Supplier A", null));

        result.Success.Should().BeTrue();
        result.Data!.LotCode.Should().Be("LOT-001");
        result.Data.Quantity.Should().Be(0);
        result.Data.AverageWeightGram.Should().BeNull();
    }

    [Fact]
    public async Task GetLots_ComputesQuantityAndAverageWeight()
    {
        var lotId = Guid.NewGuid();
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CrabLot { Id = lotId, LotCode = "L1", ImportDate = DateTime.UtcNow } });

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
        lot.Quantity.Should().Be(3);
        lot.AverageWeightGram.Should().Be(150m);
    }

    [Fact]
    public async Task CreateLot_DuplicateCode_ThrowsConflict()
    {
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabLot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _uow.Setup(u => u.CrabLots).Returns(lotRepo.Object);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            Create().CreateLotAsync(new CreateCrabLotRequest("LOT-001", DateTime.UtcNow, null, null)));
        ex.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CreateBatch_SetsActiveStatus()
    {
        var batchRepo = new Mock<IRepository<CropBatch>>();
        batchRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CropBatch, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        batchRepo.Setup(r => r.AddAsync(It.IsAny<CropBatch>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.CropBatches).Returns(batchRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().CreateBatchAsync(new CreateCropBatchRequest("BATCH-01", DateTime.UtcNow, "vụ 1"));
        result.Data!.Status.Should().Be("active");
    }
}
