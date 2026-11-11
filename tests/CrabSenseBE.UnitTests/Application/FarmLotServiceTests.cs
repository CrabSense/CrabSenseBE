using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public class FarmLotServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPublicImageStorage> _images = new();
    private FarmLotService Create() => new(_uow.Object, _images.Object, currentUser: null);

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

    [Fact]
    public async Task UploadImages_StoresDriveUrlOnLot()
    {
        var lotId = Guid.NewGuid();
        var lot = new CrabLot { Id = lotId, LotCode = "L1", Name = "Lô", ImageUrlsJson = "[]" };
        var lots = new Mock<IRepository<CrabLot>>();
        lots.Setup(r => r.GetByIdAsync(lotId, It.IsAny<CancellationToken>())).ReturnsAsync(lot);
        lots.Setup(r => r.Update(It.IsAny<CrabLot>()));
        var media = new Mock<IRepository<MediaAsset>>();
        media.Setup(r => r.AddAsync(It.IsAny<MediaAsset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Crab>());
        _uow.Setup(u => u.CrabLots).Returns(lots.Object);
        _uow.Setup(u => u.MediaAssets).Returns(media.Object);
        _uow.Setup(u => u.Crabs).Returns(crabs.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _images.SetupGet(s => s.ProviderName).Returns("GoogleDrive");
        _images.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), "a.jpg", "image/jpeg", "NhapHang/L1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaUploadResult(
                "drive-file",
                "https://drive.google.com/file/d/drive-file/view",
                "https://drive.google.com/uc?export=view&id=drive-file",
                "https://drive.google.com/uc?export=view&id=drive-file",
                12));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await Create().UploadImagesAsync(
            lotId,
            new[] { new CrabImageFile(stream, "a.jpg", "image/jpeg") },
            Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        JsonStringList.Parse(lot.ImageUrlsJson).Should().ContainSingle()
            .Which.Should().Contain("drive-file");
    }

    [Fact]
    public async Task UpdateLot_CannotRemoveAreaWithCrab_AndDoesNotMutateLot()
    {
        var lotId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        var boxB = Guid.NewGuid();
        var lot = new CrabLot
        {
            Id = lotId,
            LotCode = "LOT-AB",
            Name = "Tên cũ",
            Quantity = 10
        };
        var lotRepo = new Mock<IRepository<CrabLot>>();
        lotRepo.Setup(repo => repo.GetByIdAsync(lotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lot);
        var links = new List<CrabLotArea>
        {
            new() { CrabLotId = lotId, FarmingAreaId = areaA },
            new() { CrabLotId = lotId, FarmingAreaId = areaB }
        };
        var linkRepo = new Mock<IRepository<CrabLotArea>>();
        linkRepo.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => links.ToList());
        var crab = new Crab
        {
            Id = Guid.NewGuid(),
            CrabLotId = lotId,
            BoxId = boxB,
            Code = "CRAB-B"
        };
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(repo => repo.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { crab });
        _uow.Setup(unit => unit.CrabLots).Returns(lotRepo.Object);
        _uow.Setup(unit => unit.CrabLotAreas).Returns(linkRepo.Object);
        _uow.Setup(unit => unit.FarmingRows).Returns(RepoWithAll(
            new FarmingRow { Id = rowB, FarmingAreaId = areaB }).Object);
        _uow.Setup(unit => unit.Boxes).Returns(RepoWithAll(
            new Box { Id = boxB, FarmingRowId = rowB, Code = "BOX-B" }).Object);
        _uow.Setup(unit => unit.Crabs).Returns(crabRepo.Object);
        _uow.Setup(unit => unit.CrabBoxAllocations).Returns(RepoWithAll<CrabBoxAllocation>().Object);
        _uow.Setup(unit => unit.MoltingRecords).Returns(RepoWithAll<MoltingRecord>().Object);
        _uow.Setup(unit => unit.CrabMortalityRecords).Returns(RepoWithAll<CrabMortalityRecord>().Object);
        _uow.Setup(unit => unit.HarvestLines).Returns(RepoWithAll<HarvestLine>().Object);

        var act = () => Create().UpdateLotAsync(lotId, new UpdateCrabLotRequest(
            Name: "Tên không được lưu",
            FarmingAreaIds: new[] { areaA }));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(409);
        lot.Name.Should().Be("Tên cũ");
        links.Should().HaveCount(2);
        _uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IRepository<T>> RepoWithAll<T>(params T[] items)
        where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        return repository;
    }
}
