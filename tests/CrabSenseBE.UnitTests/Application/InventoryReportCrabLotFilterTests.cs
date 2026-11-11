using System.Linq.Expressions;
using CrabSenseBE.Application.DTOs.Reports;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public class InventoryReportCrabLotFilterTests
{
    [Fact]
    public async Task Report_WithCrabLotFilter_CountsOnlyMatchingItemsInMixedFrozenLot()
    {
        var selectedCrabLotId = Guid.NewGuid();
        var otherCrabLotId = Guid.NewGuid();
        var frozenLotId = Guid.NewGuid();
        var asOfDate = new DateTime(2026, 10, 15);
        var expiryDate = asOfDate.AddDays(10);

        var selectedCrab1 = new Crab
        {
            Id = Guid.NewGuid(),
            CrabLotId = selectedCrabLotId,
            Code = "CRAB-A1"
        };

        var selectedCrab2 = new Crab
        {
            Id = Guid.NewGuid(),
            CrabLotId = selectedCrabLotId,
            Code = "CRAB-A2"
        };

        var otherLotCrab = new Crab
        {
            Id = Guid.NewGuid(),
            CrabLotId = otherCrabLotId,
            Code = "CRAB-B1"
        };

        var frozenLot = new FrozenLot
        {
            Id = frozenLotId,
            LotCode = "FROZEN-MIXED-01",
            FrozenDate = asOfDate,
            ExpiryDate = expiryDate,
            WeightKg = 6m,
            Quantity = 3,
            Grade = "L",
            Status = FrozenLotStatus.Available
        };

        var frozenItems = new List<FrozenCrabItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FrozenLotId = frozenLotId,
                CrabId = selectedCrab1.Id,
                LotCode = frozenLot.LotCode,
                FrozenDate = asOfDate,
                ExpiryDate = expiryDate,
                WeightGram = 1700m,
                Grade = "M"
            },
            new()
            {
                Id = Guid.NewGuid(),
                FrozenLotId = frozenLotId,
                CrabId = selectedCrab2.Id,
                LotCode = frozenLot.LotCode,
                FrozenDate = asOfDate,
                ExpiryDate = expiryDate,
                WeightGram = 2100m,
                Grade = "S"
            },
            new()
            {
                Id = Guid.NewGuid(),
                FrozenLotId = frozenLotId,
                CrabId = otherLotCrab.Id,
                LotCode = frozenLot.LotCode,
                FrozenDate = asOfDate,
                ExpiryDate = expiryDate,
                WeightGram = 2200m,
                Grade = "L"
            }
        };

        var uow = CreateUnitOfWork(
            frozenLots: new[] { frozenLot },
            crabs: new[] { selectedCrab1, selectedCrab2 },
            frozenItems,
            crabLotExists: true);

        var service = new InventoryReportService(uow.Object);

        var result = await service.GetInventoryReportAsync(
            new InventoryReportFilterDto(
                AsOfDate: asOfDate,
                ExpiryWarningDays: 30,
                CrabLotId: selectedCrabLotId));

        // Chỉ 2 cua thuộc lô nhập đã chọn được tính,
        // dù cả 3 cua nằm chung trong một lô cấp đông.
        result.TotalInventoryLots.Should().Be(1);
        result.TotalInventoryQuantity.Should().Be(2);
        result.TotalInventoryWeightKg.Should().Be(3.8m);

        result.AvailableLots.Should().Be(1);
        result.ReservedLots.Should().Be(0);
        result.ShippedLots.Should().Be(0);
        result.ExpiredLots.Should().Be(0);

        // Grade và khối lượng được lấy từ các item phù hợp,
        // không lấy grade/tổng khối lượng của toàn lô đông.
        result.ByGrade.Should().HaveCount(2);
        result.ByGrade.Should().ContainSingle(x => x.Grade == "M")
            .Which.WeightKg.Should().Be(1.7m);
        result.ByGrade.Should().ContainSingle(x => x.Grade == "S")
            .Which.WeightKg.Should().Be(2.1m);
        result.ByGrade.Should().NotContain(x => x.Grade == "L");

        // Cảnh báo và xu hướng cũng chỉ tính hai cua thuộc lô đã chọn.
        result.ExpiryAlerts.Sum(x => x.Quantity).Should().Be(2);
        result.ExpiryAlerts.Sum(x => x.WeightKg).Should().Be(3.8m);

        result.Trend.Should().ContainSingle();
        result.Trend.Single().Quantity.Should().Be(2);
        result.Trend.Single().WeightKg.Should().Be(3.8m);
        result.Trend.Single().LotCount.Should().Be(1);
    }

    [Fact]
    public async Task Report_WithoutCrabLotFilter_PreservesFrozenLotTotals()
    {
        var frozenLot = new FrozenLot
        {
            Id = Guid.NewGuid(),
            LotCode = "FROZEN-MIXED-01",
            FrozenDate = new DateTime(2026, 10, 15),
            ExpiryDate = new DateTime(2026, 12, 15),
            WeightKg = 6m,
            Quantity = 3,
            Grade = "L",
            Status = FrozenLotStatus.Available
        };

        var uow = CreateUnitOfWork(
            frozenLots: new[] { frozenLot });

        var service = new InventoryReportService(uow.Object);

        var result = await service.GetInventoryReportAsync(
            new InventoryReportFilterDto(
                AsOfDate: new DateTime(2026, 10, 15),
                ExpiryWarningDays: 30));

        // Không chọn lô nhập thì giữ cách tính cũ từ FrozenLot.
        result.TotalInventoryLots.Should().Be(1);
        result.TotalInventoryQuantity.Should().Be(3);
        result.TotalInventoryWeightKg.Should().Be(6m);

        result.ByGrade.Should().ContainSingle();
        result.ByGrade.Single().Grade.Should().Be("L");
        result.ByGrade.Single().Quantity.Should().Be(3);
        result.ByGrade.Single().WeightKg.Should().Be(6m);

        result.Trend.Should().ContainSingle();
        result.Trend.Single().Quantity.Should().Be(3);
        result.Trend.Single().WeightKg.Should().Be(6m);
    }

    [Fact]
    public async Task HistoricalReport_UsesVerifiedOpeningAndOnlyMovementsBeforeCutoff()
    {
        var asOf = new DateTime(2026, 10, 15);
        var lot = new FrozenLot
        {
            Id = Guid.NewGuid(),
            LotCode = "FROZEN-HISTORY",
            FrozenDate = asOf.AddDays(-10),
            ExpiryDate = asOf.AddMonths(2),
            Quantity = 7,
            WeightKg = 7m,
            Grade = "L",
            Status = FrozenLotStatus.Available
        };
        var uow = CreateUnitOfWork(new[] { lot });
        var openingRepo = new Mock<IRepository<FrozenInventoryOpeningBalance>>();
        openingRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FrozenInventoryOpeningBalance
                {
                    FrozenLotId = lot.Id,
                    OpeningDate = asOf.AddDays(-5),
                    OpeningQuantity = 10,
                    OpeningWeightKg = 10m,
                    VerificationStatus = FrozenOpeningBalanceVerificationStatus.Verified
                }
            });
        var adjustmentRepo = new Mock<IRepository<FrozenInventoryAdjustment>>();
        adjustmentRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FrozenInventoryAdjustment
                {
                    FrozenLotId = lot.Id,
                    QuantityDelta = -3,
                    WeightDeltaKg = -3m,
                    CreatedAt = asOf.AddDays(1)
                }
            });
        uow.Setup(x => x.FrozenInventoryOpeningBalances).Returns(openingRepo.Object);
        uow.Setup(x => x.FrozenInventoryAdjustments).Returns(adjustmentRepo.Object);

        var result = await new InventoryReportService(uow.Object)
            .GetInventoryReportAsync(new InventoryReportFilterDto(asOf, 30));

        result.TotalInventoryQuantity.Should().Be(10);
        result.TotalInventoryWeightKg.Should().Be(10m);
        result.DataStatus.Should().Be("Complete");
    }

    private static Mock<IUnitOfWork> CreateUnitOfWork(
        IEnumerable<FrozenLot> frozenLots,
        IEnumerable<Crab>? crabs = null,
        IEnumerable<FrozenCrabItem>? frozenItems = null,
        bool crabLotExists = false)
    {
        var uow = new Mock<IUnitOfWork>();

        var frozenLotRepository = new Mock<IRepository<FrozenLot>>();
        frozenLotRepository
            .Setup(repository =>
                repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(frozenLots);

        uow.Setup(unit => unit.FrozenLots)
            .Returns(frozenLotRepository.Object);

        if (crabs is not null)
        {
            var crabRepository = new Mock<IRepository<Crab>>();
            crabRepository
                .Setup(repository => repository.FindAsync(
                    It.IsAny<Expression<Func<Crab, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(crabs);

            uow.Setup(unit => unit.Crabs)
                .Returns(crabRepository.Object);
        }

        if (frozenItems is not null)
        {
            var frozenItemRepository =
                new Mock<IRepository<FrozenCrabItem>>();

            frozenItemRepository
                .Setup(repository => repository.FindAsync(
                    It.IsAny<Expression<Func<FrozenCrabItem, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(frozenItems);

            uow.Setup(unit => unit.FrozenCrabItems)
                .Returns(frozenItemRepository.Object);
        }

        if (crabs is not null)
        {
            var crabLotRepository = new Mock<IRepository<CrabLot>>();
            crabLotRepository
                .Setup(repository => repository.AnyAsync(
                    It.IsAny<Expression<Func<CrabLot, bool>>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(crabLotExists);

            uow.Setup(unit => unit.CrabLots)
                .Returns(crabLotRepository.Object);
        }

        return uow;
    }
}