using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: Các service báo cáo — Harvest, Inventory.
/// Chỉ test các service dùng FindAsync/GetAllAsync (không dùng Query().Include().ToListAsync()).
/// </summary>
public class ReportsServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();

    // ─────────────────────────────────────────────────────────────────────────
    // HarvestReportService
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HarvestReport_ReturnsCorrectTotals()
    {
        var voucherId1 = Guid.NewGuid();
        var voucherId2 = Guid.NewGuid();

        var vouchers = new List<HarvestVoucher>
        {
            new() { Id = voucherId1, VoucherCode = "HV-1", Status = HarvestStatus.Completed, HarvestDate = DateTime.UtcNow.AddDays(-2), TotalQuantity = 5, TotalWeightKg = 10, CreatedBy = Guid.NewGuid() },
            new() { Id = voucherId2, VoucherCode = "HV-2", Status = HarvestStatus.Completed, HarvestDate = DateTime.UtcNow.AddDays(-1), TotalQuantity = 3, TotalWeightKg = 6, CreatedBy = Guid.NewGuid() }
        };

        var lines = new List<HarvestLine>
        {
            new() { HarvestVoucherId = voucherId1, WeightGram = 5000, Grade = "M", IsSoftshell = true },
            new() { HarvestVoucherId = voucherId1, WeightGram = 5000, Grade = "L", IsSoftshell = false },
            new() { HarvestVoucherId = voucherId2, WeightGram = 3000, Grade = "M", IsSoftshell = true },
            new() { HarvestVoucherId = voucherId2, WeightGram = 3000, Grade = "S", IsSoftshell = false }
        };

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestVoucher, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(vouchers);

        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lines);

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.HarvestLines).Returns(lineRepo.Object);

        var service = new HarvestReportService(_uow.Object);
        var result = await service.GetHarvestReportAsync(new HarvestReportFilterDto());

        result.TotalVouchers.Should().Be(2);
        result.TotalQuantity.Should().Be(8);
        result.TotalWeightKg.Should().Be(16);
        result.ByGrade.Should().HaveCount(3);
        result.Softshell.WeightKg.Should().Be(8);
    }

    [Fact]
    public async Task HarvestReport_NoCompletedVouchers_ReturnsZeros()
    {
        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestVoucher, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HarvestVoucher>());

        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<HarvestLine>());

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.HarvestLines).Returns(lineRepo.Object);

        var service = new HarvestReportService(_uow.Object);
        var result = await service.GetHarvestReportAsync(new HarvestReportFilterDto());

        result.TotalVouchers.Should().Be(0);
        result.TotalQuantity.Should().Be(0);
        result.ByGrade.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InventoryReportService
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InventoryReport_CountsByStatus()
    {
        var lots = new List<FrozenLot>
        {
            new() { Id = Guid.NewGuid(), LotCode = "L1", Status = FrozenLotStatus.Available, WeightKg = 10, Quantity = 5, Grade = "M", ExpiryDate = DateTime.UtcNow.AddMonths(6) },
            new() { Id = Guid.NewGuid(), LotCode = "L2", Status = FrozenLotStatus.Reserved, WeightKg = 8, Quantity = 3, Grade = "S", ExpiryDate = DateTime.UtcNow.AddMonths(6) },
            new() { Id = Guid.NewGuid(), LotCode = "L3", Status = FrozenLotStatus.Shipped, WeightKg = 5, Quantity = 2, Grade = "M", ExpiryDate = DateTime.UtcNow.AddMonths(6) }
        };

        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lots);

        _uow.Setup(u => u.FrozenLots).Returns(lotRepo.Object);

        var service = new InventoryReportService(_uow.Object);
        var result = await service.GetInventoryReportAsync();

        result.AvailableLots.Should().Be(1);
        result.ReservedLots.Should().Be(1);
        result.ShippedLots.Should().Be(1);
        result.ExpiredLots.Should().Be(0);
        result.TotalInventoryLots.Should().Be(2); // Available + Reserved
    }

    [Fact]
    public async Task InventoryReport_EmptyWarehouse_ReturnsZeros()
    {
        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FrozenLot>());

        _uow.Setup(u => u.FrozenLots).Returns(lotRepo.Object);

        var service = new InventoryReportService(_uow.Object);
        var result = await service.GetInventoryReportAsync();

        result.TotalInventoryLots.Should().Be(0);
        result.AvailableLots.Should().Be(0);
        result.ShippedLots.Should().Be(0);
    }
    [Fact]
public async Task HarvestReport_WithCrabLotFilter_OnlyCountsLinesFromSelectedLot()
{
    var selectedLotId = Guid.NewGuid();
    var otherLotId = Guid.NewGuid();
    var voucherId = Guid.NewGuid();

    var selectedCrab1 = new Crab
    {
        Id = Guid.NewGuid(),
        CrabLotId = selectedLotId,
        Code = "CRAB-001"
    };

    var selectedCrab2 = new Crab
    {
        Id = Guid.NewGuid(),
        CrabLotId = selectedLotId,
        Code = "CRAB-002"
    };

    var otherLotCrab = new Crab
    {
        Id = Guid.NewGuid(),
        CrabLotId = otherLotId,
        Code = "CRAB-003"
    };

    var harvestDate = new DateTime(2026, 10, 15);

    var vouchers = new List<HarvestVoucher>
    {
        new()
        {
            Id = voucherId,
            VoucherCode = "HV-MIXED",
            Status = HarvestStatus.Completed,
            HarvestDate = harvestDate,
            TotalQuantity = 3,
            TotalWeightKg = 6,
            CreatedBy = Guid.NewGuid()
        }
    };

    var lines = new List<HarvestLine>
    {
        new()
        {
            HarvestVoucherId = voucherId,
            CrabId = selectedCrab1.Id,
            WeightGram = 1800,
            Grade = "M",
            IsSoftshell = true
        },
        new()
        {
            HarvestVoucherId = voucherId,
            CrabId = selectedCrab2.Id,
            WeightGram = 2200,
            Grade = "L",
            IsSoftshell = false
        },
        new()
        {
            HarvestVoucherId = voucherId,
            CrabId = otherLotCrab.Id,
            WeightGram = 2000,
            Grade = "S",
            IsSoftshell = true
        }
    };

    var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
    voucherRepo
        .Setup(r => r.FindAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<HarvestVoucher, bool>>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(vouchers);

    var lineRepo = new Mock<IRepository<HarvestLine>>();
    lineRepo
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(lines);

    var crabRepo = new Mock<IRepository<Crab>>();
    crabRepo
        .Setup(r => r.FindAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<Crab> { selectedCrab1, selectedCrab2 });

    _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
    _uow.Setup(u => u.HarvestLines).Returns(lineRepo.Object);
    _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);

    var service = new HarvestReportService(_uow.Object);

    var result = await service.GetHarvestReportAsync(
        new HarvestReportFilterDto
        {
            CrabLotId = selectedLotId
        });

    result.TotalVouchers.Should().Be(1);
    result.TotalQuantity.Should().Be(2);
    result.TotalWeightKg.Should().Be(4m);

    result.ByDate.Should().ContainSingle();
    result.ByDate[0].Quantity.Should().Be(2);
    result.ByDate[0].WeightKg.Should().Be(4m);

    result.ByGrade.Should().HaveCount(2);
    result.ByGrade.Should().NotContain(grade => grade.Grade == "S");

    result.Softshell.WeightKg.Should().Be(1.8m);
}
}