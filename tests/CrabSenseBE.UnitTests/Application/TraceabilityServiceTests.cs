using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: TraceabilityService — QR cho FrozenLot/HarvestVoucher + traceability public.</summary>
public class TraceabilityServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();

    private TraceabilityService Create() => new(_uow.Object, currentUser: null);

    [Fact]
    public async Task Product_qr_workflow_enforces_two_area_scope_self_approval_and_transitions()
    {
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var managerA = Guid.NewGuid();
        var staffA = Guid.NewGuid();
        var lotA = new FrozenLot
        {
            Id = Guid.NewGuid(), LotCode = "FZ-A", FarmingAreaId = areaA,
            Quantity = 8, WeightKg = 2
        };
        var lotB = new FrozenLot
        {
            Id = Guid.NewGuid(), LotCode = "FZ-B", FarmingAreaId = areaB,
            Quantity = 5, WeightKg = 1
        };
        var activeUser = staffA;
        var current = new Mock<CrabSenseBE.Application.Interfaces.ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(() => activeUser);
        var usersById = new Dictionary<Guid, AppUser>
        {
            [managerA] = new()
            {
                Id = managerA, Username = "manager-a",
                Role = UserRole.FarmManager, IsActive = true
            },
            [staffA] = new()
            {
                Id = staffA, Username = "staff-a",
                Role = UserRole.FarmStaff, IsActive = true
            }
        };
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => usersById.GetValueOrDefault(id));
        var managerAssignments = new Mock<IRepository<FarmManagerAssignment>>();
        managerAssignments.Setup(repo => repo.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmManagerAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmManagerAssignment { ManagerUserId = managerA, FarmingAreaId = areaA }
            });
        var staffAssignments = new Mock<IRepository<FarmStaffAssignment>>();
        staffAssignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = staffA, ManagerUserId = managerA, FarmingAreaId = areaA
            });
        var lots = new List<FrozenLot> { lotA, lotB };
        var lotRepo = new Mock<IRepository<FrozenLot>>();
        lotRepo.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => lots.SingleOrDefault(item => item.Id == id));
        var qrCodes = new List<QrCode>();
        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(repo => repo.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => qrCodes.SingleOrDefault(item => item.Id == id));
        qrRepo.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<QrCode, bool>> predicate,
                CancellationToken _) => qrCodes.FirstOrDefault(predicate.Compile()));
        qrRepo.Setup(repo => repo.AnyAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<QrCode, bool>> predicate,
                CancellationToken _) => qrCodes.Any(predicate.Compile()));
        qrRepo.Setup(repo => repo.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<QrCode, bool>> predicate,
                CancellationToken _) => qrCodes.Where(predicate.Compile()).ToList());
        qrRepo.Setup(repo => repo.AddAsync(It.IsAny<QrCode>(), It.IsAny<CancellationToken>()))
            .Callback<QrCode, CancellationToken>((item, _) => qrCodes.Add(item))
            .Returns(Task.CompletedTask);
        qrRepo.Setup(repo => repo.Update(It.IsAny<QrCode>()));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmManagerAssignments).Returns(managerAssignments.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(staffAssignments.Object);
        uow.Setup(unit => unit.FrozenLots).Returns(lotRepo.Object);
        uow.Setup(unit => unit.QrCodes).Returns(qrRepo.Object);
        var auditLogs = new List<OperationLog>();
        var operationLogs = new Mock<IRepository<OperationLog>>();
        operationLogs.Setup(repo => repo.AddAsync(
                It.IsAny<OperationLog>(), It.IsAny<CancellationToken>()))
            .Callback<OperationLog, CancellationToken>((item, _) => auditLogs.Add(item))
            .Returns(Task.CompletedTask);
        uow.Setup(unit => unit.OperationLogs).Returns(operationLogs.Object);
        var saves = 0;
        uow.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saves++)
            .ReturnsAsync(1);
        var service = new TraceabilityService(uow.Object, current.Object);

        var proposal = await service.EnsureFrozenLotQrAsync(lotA.Id);
        proposal.Data!.ApprovalStatus.Should().Be("Pending");
        var qrA = qrCodes.Single();
        qrA.ProposedByUserId.Should().Be(staffA);

        var foreignProposal = () => service.EnsureFrozenLotQrAsync(lotB.Id);
        (await foreignProposal.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        qrCodes.Should().ContainSingle();

        var pendingPublic = () => service.GetTraceabilityAsync(qrA.Code);
        (await pendingPublic.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(404);
        qrA.ScanCount.Should().Be(0);

        activeUser = managerA;
        var emptyReason = () => service.ApproveQrAsync(
            qrA.Id, new ApproveQrAssignmentRequest(" "));
        (await emptyReason.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(400);
        qrA.ApprovalStatus.Should().Be(QrApprovalStatus.Pending);
        var approval = new ApproveQrAssignmentRequest("Đã đối chiếu hồ sơ");
        await service.ApproveQrAsync(qrA.Id, approval);
        qrA.ApprovalStatus.Should().Be(QrApprovalStatus.Approved);
        qrA.DecisionReason.Should().Be(approval.Reason);
        auditLogs.Should().ContainSingle(log =>
            log.Action == "ApproveProductQr"
            && log.UserId == managerA
            && log.EntityId == qrA.Id);
        var savesAfterApproval = saves;
        var approveTwice = () => service.ApproveQrAsync(qrA.Id, approval);
        (await approveTwice.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        saves.Should().Be(savesAfterApproval);

        var publicResult = await service.GetTraceabilityAsync(qrA.Code);
        publicResult.Data!.FrozenLotCode.Should().Be(lotA.LotCode);
        qrA.ScanCount.Should().Be(1);
        qrA.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        var expiredPublic = () => service.GetTraceabilityAsync(qrA.Code);
        (await expiredPublic.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(404);
        qrA.ScanCount.Should().Be(1, "expired QR scans must not be counted");
        qrA.ExpiresAt = null;

        var selfQr = new QrCode
        {
            Id = Guid.NewGuid(), Code = "SELF-QR", EntityType = "frozen_lot",
            FrozenLotId = lotA.Id, IsActive = true,
            ApprovalStatus = QrApprovalStatus.Pending, ProposedByUserId = managerA
        };
        qrCodes.Add(selfQr);
        var savesBeforeSelfApproval = saves;
        var selfApprove = () => service.ApproveQrAsync(selfQr.Id, approval);
        (await selfApprove.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        selfQr.ApprovalStatus.Should().Be(QrApprovalStatus.Pending);
        saves.Should().Be(savesBeforeSelfApproval);

        var foreignQr = new QrCode
        {
            Id = Guid.NewGuid(), Code = "FOREIGN-QR", EntityType = "frozen_lot",
            FrozenLotId = lotB.Id, IsActive = true,
            ApprovalStatus = QrApprovalStatus.Pending, ProposedByUserId = Guid.NewGuid()
        };
        qrCodes.Add(foreignQr);
        var foreignApprove = () => service.ApproveQrAsync(foreignQr.Id, approval);
        (await foreignApprove.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        foreignQr.ApprovalStatus.Should().Be(QrApprovalStatus.Pending);

        var pendingReview = await service.GetPendingReviewAsync();
        pendingReview.Data.Should().ContainSingle(item => item.Id == selfQr.Id);
        pendingReview.Data.Should().NotContain(item => item.Id == foreignQr.Id);

        var rejectedQr = new QrCode
        {
            Id = Guid.NewGuid(), Code = "REJECT-QR", EntityType = "frozen_lot",
            FrozenLotId = lotA.Id, IsActive = true,
            ApprovalStatus = QrApprovalStatus.Pending, ProposedByUserId = staffA
        };
        qrCodes.Add(rejectedQr);
        var lotQuantity = lotA.Quantity;
        var lotWeight = lotA.WeightKg;
        await service.RejectQrAsync(rejectedQr.Id, new RejectQrAssignmentRequest("Sai nhãn"));
        rejectedQr.ApprovalStatus.Should().Be(QrApprovalStatus.Rejected);
        auditLogs.Should().ContainSingle(log =>
            log.Action == "RejectProductQr"
            && log.UserId == managerA
            && log.Details!.Contains("Reason=Sai nhãn"));
        lotA.Quantity.Should().Be(lotQuantity);
        lotA.WeightKg.Should().Be(lotWeight);
    }

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
        result.Data.ApprovalStatus.Should().Be("Pending");
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
        result.Data.ApprovalStatus.Should().Be("Pending");
    }

    [Theory]
    [InlineData(HarvestStatus.PendingApproval)]
    [InlineData(HarvestStatus.Approved)]
    [InlineData(HarvestStatus.InProgress)]
    public async Task EnsureHarvestVoucherQr_RejectsVoucherBeforeFinalConfirmation(
        HarvestStatus status)
    {
        var voucherId = Guid.NewGuid();
        var voucherRepo = new Mock<IRepository<HarvestVoucher>>();
        voucherRepo.Setup(repo => repo.GetByIdAsync(
                voucherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HarvestVoucher
            {
                Id = voucherId,
                VoucherCode = "HV-NOT-COMPLETED",
                Status = status
            });
        _uow.Setup(unit => unit.HarvestVouchers).Returns(voucherRepo.Object);

        var act = () => Create().EnsureHarvestVoucherQrAsync(voucherId);

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(409);
        _uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
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
        lotRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FrozenLot, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { lot });

        var lineRepo = new Mock<IRepository<HarvestLine>>();
        lineRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<HarvestLine, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { firstLine });

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
        result.Data.FrozenLots.Should().ContainSingle();
        result.Data.Grades.Should().Equal("L");

        var second = new FrozenLot { LotCode = "FL-002", HarvestVoucherId = voucherId, Grade = "M" };
        lotRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FrozenLot, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { lot, second });
        var mixed = await Create().GetTraceabilityAsync("HV-TEST-ABCDEF");
        mixed.Data!.FrozenLotCode.Should().BeNull();
        mixed.Data.Grade.Should().Be("Mixed");
        mixed.Data.FrozenLots!.Select(l => l.LotCode).Should().Equal("FL-001", "FL-002");
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

    [Fact]
    public async Task GetTraceability_PendingProductQr_IsNotPublished()
    {
        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QrCode
            {
                Code = "FL-PENDING",
                EntityType = "frozen_lot",
                FrozenLotId = Guid.NewGuid(),
                IsActive = true,
                ApprovalStatus = Domain.Enums.QrApprovalStatus.Pending
            });
        _uow.Setup(u => u.QrCodes).Returns(qrRepo.Object);

        await Assert.ThrowsAsync<AppException>(() =>
            Create().GetTraceabilityAsync("FL-PENDING"));
    }

    [Fact]
    public async Task GetTraceability_RejectedProductQr_IsNotPublished()
    {
        var qrRepo = new Mock<IRepository<QrCode>>();
        qrRepo.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QrCode, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QrCode
            {
                Code = "FL-REJECTED",
                EntityType = "frozen_lot",
                FrozenLotId = Guid.NewGuid(),
                IsActive = true,
                ApprovalStatus = QrApprovalStatus.Rejected
            });
        _uow.Setup(unit => unit.QrCodes).Returns(qrRepo.Object);

        var act = () => Create().GetTraceabilityAsync("FL-REJECTED");

        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(404);
        _uow.Verify(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
