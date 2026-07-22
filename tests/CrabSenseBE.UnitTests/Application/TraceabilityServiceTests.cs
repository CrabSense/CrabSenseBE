using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: TraceabilityService — QR cho FrozenLot/HarvestVoucher + traceability public.</summary>
public class TraceabilityServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();

    private TraceabilityService Create() => new(_uow.Object);

    // ─────────────────────────────────────────────────────────────────────────
    // EnsureFrozenLotQrAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureFrozenLotQr_CreatesNewCode()
    {
        var lotId = Guid.NewGuid();
        var lot = new FrozenLot
        {
            Id = lotId,
            LotCode = "FL-001",
            FrozenDate = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            WeightKg = 10,
            Quantity = 20,
            Status = Domain.Enums.FrozenLotStatus.Available
        };

        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(r => r.GetByIdAsync(lotId, It.IsAny<CancellationToken>())).ReturnsAsync(lot);

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QrCode?)null);
        qrRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        qrRepo.Setup(r => r.AddAsync(It.IsAny<QrCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.FrozenLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().EnsureFrozenLotQrAsync(lotId);

        result.Success.Should().BeTrue();
        result.Data!.Code.Should().StartWith("FL-FL-001-");
    }

    [Fact]
    public async Task EnsureFrozenLotQr_Existing_ReturnsExisting()
    {
        var lotId = Guid.NewGuid();
        var existingQr = new QrCode
        {
            Code = "FL-EXISTING",
            EntityType = "frozen_lot",
            FrozenLotId = lotId,
            IsActive = true
        };

        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(r => r.GetByIdAsync(lotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FrozenLot { Id = lotId, LotCode = "FL-001" });

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingQr);

        _uow.Setup(u => u.FrozenLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);

        var result = await Create().EnsureFrozenLotQrAsync(lotId);

        result.Success.Should().BeTrue();
        result.Data!.Code.Should().Be("FL-EXISTING");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EnsureHarvestVoucherQrAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureHarvestVoucherQr_CreatesNewCode()
    {
        var voucherId = Guid.NewGuid();
        var voucher = new HarvestVoucher
        {
            Id = voucherId,
            VoucherCode = "HV-20260101-ABC",
            HarvestDate = DateTime.UtcNow,
            Status = Domain.Enums.HarvestStatus.Completed,
            CreatedBy = Guid.NewGuid()
        };

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.GetByIdAsync(voucherId, It.IsAny<CancellationToken>())).ReturnsAsync(voucher);

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QrCode?)null);
        qrRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        qrRepo.Setup(r => r.AddAsync(It.IsAny<QrCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().EnsureHarvestVoucherQrAsync(voucherId);

        result.Success.Should().BeTrue();
        result.Data!.Code.Should().StartWith("HV-HV-20260101-ABC-");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetTraceabilityAsync — frozen_lot
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTraceability_FrozenLot_ReturnsLinkedData()
    {
        var lotId = Guid.NewGuid();
        var voucherId = Guid.NewGuid();
        var qr = new QrCode
        {
            Code = "FL-001-ABCDEF",
            EntityType = "frozen_lot",
            FrozenLotId = lotId,
            IsActive = true,
            ScanCount = 0
        };
        var lot = new FrozenLot
        {
            Id = lotId,
            LotCode = "FL-001",
            HarvestVoucherId = voucherId,
            FrozenDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            Grade = "M"
        };
        var voucher = new HarvestVoucher
        {
            Id = voucherId,
            VoucherCode = "HV-TEST",
            HarvestDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(qr);
        qrRepo.Setup(r => r.Update(It.IsAny<QrCode>()));

        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(r => r.GetByIdAsync(lotId, It.IsAny<CancellationToken>())).ReturnsAsync(lot);

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.GetByIdAsync(voucherId, It.IsAny<CancellationToken>())).ReturnsAsync(voucher);

        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.FrozenLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().GetTraceabilityAsync("FL-001-ABCDEF");

        result.Success.Should().BeTrue();
        result.Data!.Code.Should().Be("FL-001-ABCDEF");
        result.Data.FrozenLotCode.Should().Be("FL-001");
        result.Data.HarvestVoucherCode.Should().Be("HV-TEST");
        result.Data.Grade.Should().Be("M");
        qr.ScanCount.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetTraceabilityAsync — harvest_voucher
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTraceability_HarvestVoucher_ReturnsLinkedData()
    {
        var voucherId = Guid.NewGuid();
        var lotId = Guid.NewGuid();
        var qr = new QrCode
        {
            Code = "HV-TEST-ABCDEF",
            EntityType = "harvest_voucher",
            HarvestVoucherId = voucherId,
            IsActive = true,
            ScanCount = 0
        };
        var voucher = new HarvestVoucher
        {
            Id = voucherId,
            VoucherCode = "HV-TEST",
            HarvestDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Status = Domain.Enums.HarvestStatus.Completed,
            CreatedBy = Guid.NewGuid()
        };
        var lot = new FrozenLot
        {
            Id = lotId,
            LotCode = "FL-001",
            HarvestVoucherId = voucherId,
            FrozenDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var firstLine = new HarvestLine
        {
            HarvestVoucherId = voucherId,
            Grade = "L"
        };

        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(qr);
        qrRepo.Setup(r => r.Update(It.IsAny<QrCode>()));

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.GetByIdAsync(voucherId, It.IsAny<CancellationToken>())).ReturnsAsync(voucher);

        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FrozenLot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lot);

        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestLine, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstLine);

        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);
        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.FrozenLots).Returns(lotRepo.Object);
        _uow.Setup(u => u.HarvestLines).Returns(lineRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await Create().GetTraceabilityAsync("HV-TEST-ABCDEF");

        result.Success.Should().BeTrue();
        result.Data!.HarvestVoucherCode.Should().Be("HV-TEST");
        result.Data.FrozenLotCode.Should().Be("FL-001");
        result.Data.Grade.Should().Be("L");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetTraceabilityAsync — not found
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTraceability_CodeNotFound_Throws()
    {
        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QrCode?)null);

        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetTraceabilityAsync("NONEXISTENT"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetTraceabilityAsync — empty code
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTraceability_EmptyCode_Throws()
    {
        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetTraceabilityAsync(""));
    }
}
