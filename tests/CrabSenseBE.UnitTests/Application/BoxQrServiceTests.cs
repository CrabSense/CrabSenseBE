using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: QR hộp — tạo tem, quét xem cua, di dời.</summary>
public class BoxQrServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IFarmHistoryService> _history = new();

    private BoxQrService Create() => new(_uow.Object, _history.Object);

    [Fact]
    public async Task EnsureBoxQr_CreatesUniqueCode()
    {
        var boxId = Guid.NewGuid();
        var box = new Box { Id = boxId, Code = "A01" };

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>())).ReturnsAsync(box);

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QrCode?)null);
        qrRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        qrRepo.Setup(r => r.AddAsync(It.IsAny<QrCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().EnsureBoxQrAsync(boxId);

        result.Success.Should().BeTrue();
        result.Data!.BoxId.Should().Be(boxId);
        result.Data.Code.Should().StartWith("A01-");
    }

    [Fact]
    public async Task Scan_ReturnsBoxAndCrabs_IncrementsScanCount()
    {
        var boxId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var qr = new QrCode
        {
            Code = "A01-ABCDEF12",
            EntityType = "box",
            BoxId = boxId,
            IsActive = true,
            ScanCount = 0
        };
        var box = new Box { Id = boxId, Code = "A01", FarmingRowId = rowId, Status = "active", IsOccupied = true };
        var row = new FarmingRow { Id = rowId, FarmingAreaId = areaId, Name = "Dãy 1" };
        var area = new FarmingArea { Id = areaId, Name = "Khu A" };
        var crab = new Crab { Id = Guid.NewGuid(), BoxId = boxId, Tag = "C-1", IsAlive = true, WeightGram = 90 };

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(qr);
        qrRepo.Setup(r => r.Update(It.IsAny<QrCode>()));

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>())).ReturnsAsync(box);

        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetByIdAsync(rowId, It.IsAny<CancellationToken>())).ReturnsAsync(row);

        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetByIdAsync(areaId, It.IsAny<CancellationToken>())).ReturnsAsync(area);

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { crab });

        var allocRepo = new Mock<IRepository<CrabBoxAllocation>>();
        allocRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<CrabBoxAllocation>());

        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.CrabBoxAllocations).Returns(allocRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().ScanAsync("A01-ABCDEF12");

        result.Success.Should().BeTrue();
        result.Data!.AreaName.Should().Be("Khu A");
        result.Data.RowName.Should().Be("Dãy 1");
        result.Data.Crabs.Should().HaveCount(1);
        result.Data.Crabs[0].Tag.Should().Be("C-1");
        qr.ScanCount.Should().Be(1);
    }

    [Fact]
    public async Task MoveCrab_UsesTargetQr_CallsAllocate()
    {
        var sourceBox = Guid.NewGuid();
        var targetBox = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var areaId = Guid.NewGuid();
        var crabId = Guid.NewGuid();
        var sourceQr = new QrCode { Code = "SRC", EntityType = "box", BoxId = sourceBox, IsActive = true };
        var targetQr = new QrCode { Code = "DST", EntityType = "box", BoxId = targetBox, IsActive = true };
        var crab = new Crab { Id = crabId, BoxId = sourceBox, IsAlive = true };

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.SetupSequence(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceQr)
            .ReturnsAsync(targetQr);

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);

        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(targetBox, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = targetBox, FarmingRowId = rowId, Code = "DST" });

        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetByIdAsync(rowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowId, FarmingAreaId = areaId, Name = "R1", IsActive = true });

        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);

        _history.Setup(h => h.AllocateCrabAsync(It.IsAny<AllocateCrabRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<CrabBoxAllocationDto>.Ok(
                new CrabBoxAllocationDto(Guid.NewGuid(), crabId, targetBox, DateTime.UtcNow, null, null)));

        var result = await Create().MoveCrabAsync("SRC", new MoveCrabByScanRequest(crabId, null, "DST", "move"));

        result.Success.Should().BeTrue();
        _history.Verify(h => h.AllocateCrabAsync(
            It.Is<AllocateCrabRequest>(r =>
                r.CrabId == crabId
                && r.BoxId == targetBox
                && r.FarmingRowId == rowId
                && r.FarmingAreaId == areaId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCrabFromScan_WhenWrongBox_Throws()
    {
        var boxId = Guid.NewGuid();
        var qr = new QrCode { Code = "A01-X", EntityType = "box", BoxId = boxId, IsActive = true };
        var crab = new Crab { Id = Guid.NewGuid(), BoxId = Guid.NewGuid() }; // khác hộp

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(qr);
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);

        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().UpdateCrabFromScanAsync("A01-X", crab.Id, new UpdateCrabFromScanRequest(null, 100, null, null, null)));
    }
}
