using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: HarvestService — Box ↔ Harvest link endpoints.</summary>
public class HarvestServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private HarvestService Create()
    {
        _currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);
        return new HarvestService(_uow.Object, _currentUser.Object);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetBoxesByVoucherAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoxesByVoucher_VoucherNotFound_Throws()
    {
        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarvestVoucher?)null);

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetBoxesByVoucherAsync(Guid.NewGuid()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetVouchersByBoxAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVouchersByBox_BoxNotFound_Throws()
    {
        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Box?)null);

        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetVouchersByBoxAsync(Guid.NewGuid()));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CreateAsync — existing tests validate the core CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_VoucherWithLines_Succeeds()
    {
        var currentUser = Guid.NewGuid();
        _currentUser.Setup(c => c.UserId).Returns(currentUser);
        _currentUser.Setup(c => c.IsAuthenticated).Returns(true);

        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestVoucher, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        voucherRepo.Setup(r => r.AddAsync(It.IsAny<HarvestVoucher>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(r => r.AddAsync(It.IsAny<HarvestLine>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken ct) => new Crab
            {
                Id = id,
                Status = CrabStatus.Alive,
                StockedAt = DateTime.UtcNow,
                MoltingStage = "hard",
                BoxAllocations = new List<CrabBoxAllocation>()
            });

        var lineRepoFind = new Mock<IRepository<HarvestLine>>();
        lineRepoFind.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestLine, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _uow.Setup(u => u.HarvestVouchers).Returns(voucherRepo.Object);
        _uow.Setup(u => u.HarvestLines).Returns(lineRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var request = new CreateHarvestVoucherRequest(
            null,
            DateTime.UtcNow,
            "test harvest",
            new List<HarvestLineRequest>
            {
                new(Guid.NewGuid(), 250, "M", true, null)
            });

        var result = await Create().CreateAsync(request);

        result.Success.Should().BeTrue();
        result.Data!.VoucherCode.Should().StartWith("HV-");
        result.Data.Lines.Should().HaveCount(1);
    }
}
