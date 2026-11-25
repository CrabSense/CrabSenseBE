using System.Linq.Expressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Harvest;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class HarvestAssignmentServiceTests
{
    [Fact]
    public async Task AcceptAssignment_MovesAssignmentAndLinesToInProgress()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.Assigned, lineCount: 1);

        await fixture.Service.AcceptAssignmentAsync(fixture.Assignment.Id);

        Assert.Equal(HarvestAssignmentStatus.InProgress, fixture.Assignment.Status);
        Assert.NotNull(fixture.Assignment.StartedAt);
        Assert.All(fixture.Lines, line =>
            Assert.Equal(HarvestAssignmentLineStatus.InProgress, line.Status));
        fixture.UnitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAssignment_RejectsCrabThatIsNotAlive()
    {
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var areaId = Guid.NewGuid();

        var manager = new AppUser
        {
            Id = managerId,
            Role = UserRole.FarmManager,
            IsActive = true,
            FullName = "Manager"
        };

        var staff = new AppUser
        {
            Id = staffId,
            Role = UserRole.FarmStaff,
            IsActive = true,
            FullName = "Staff"
        };

        var crab = new Crab
        {
            Id = Guid.NewGuid(),
            Code = "CRAB-MOLT",
            Status = CrabStatus.Molting,
            StockedAt = DateTime.UtcNow.AddDays(-10)
        };

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.UserId).Returns(managerId);
        currentUser.Setup(x => x.IsAuthenticated).Returns(true);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.Users)
            .Returns(CreateRepository(new List<AppUser> { manager, staff }).Object);
        unitOfWork.Setup(x => x.FarmManagerAssignments)
            .Returns(CreateRepository(
            [
                new FarmManagerAssignment
                {
                    ManagerUserId = managerId,
                    FarmingAreaId = areaId,
                    AssignedByUserId = managerId
                }
            ]).Object);
        unitOfWork.Setup(x => x.FarmStaffAssignments)
            .Returns(CreateRepository(
            [
                new FarmStaffAssignment
                {
                    StaffUserId = staffId,
                    ManagerUserId = managerId,
                    FarmingAreaId = areaId,
                    AssignedByUserId = managerId
                }
            ]).Object);
        unitOfWork.Setup(x => x.Crabs)
            .Returns(CreateRepository(new List<Crab> { crab }).Object);

        var service = new HarvestAssignmentService(
            unitOfWork.Object,
            currentUser.Object,
            new HarvestRecorder(unitOfWork.Object, currentUser.Object));

        var request = new CreateHarvestAssignmentRequest(
            areaId,
            staffId,
            [crab.Id]);

        await Assert.ThrowsAsync<AppException>(() =>
            service.CreateAssignmentAsync(request));
    }

    [Fact]
    public async Task SubmitResults_SavesOutcomesWithoutCreatingVoucherOrChangingCrabStatus()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 2);

        var request = new SubmitHarvestAssignmentResultsRequest(
        [
            new HarvestAssignmentResultRequest(
                AssignmentLineId: fixture.Lines[0].Id,
                Outcome: HarvestAssignmentOutcome.Harvested,
                WeightGram: 350,
                Grade: "M",
                CarapaceLengthMm: 72,
                CarapaceWidthMm: 85,
                HarvestedAt: DateTimeOffset.UtcNow,
                PhotoUrls: ["https://example.test/crab.jpg"]),
            new HarvestAssignmentResultRequest(
                AssignmentLineId: fixture.Lines[1].Id,
                Outcome: HarvestAssignmentOutcome.ContinueRaising,
                Reason: "Chưa đủ kích thước",
                MoltConfirmed: false)
        ]);

        await fixture.Service.SubmitAssignmentResultsAsync(
            fixture.Assignment.Id,
            request);

        Assert.Equal(
            HarvestAssignmentOutcome.Harvested,
            fixture.Lines[0].Outcome);
        Assert.Equal(350, fixture.Lines[0].HarvestWeightGram);
        Assert.Equal("M", fixture.Lines[0].HarvestGrade);
        Assert.Equal(72, fixture.Lines[0].HarvestCarapaceLengthMm);
        Assert.Equal(85, fixture.Lines[0].HarvestCarapaceWidthMm);
        Assert.NotNull(fixture.Lines[0].PendingHarvestedAt);

        Assert.Equal(
            HarvestAssignmentOutcome.ContinueRaising,
            fixture.Lines[1].Outcome);
        Assert.Equal(
            "Chưa đủ kích thước",
            fixture.Lines[1].EligibilityReason);

        Assert.All(fixture.Lines, line =>
            Assert.Equal(HarvestAssignmentLineStatus.InProgress, line.Status));
        Assert.All(fixture.Crabs, crab =>
            Assert.Equal(CrabStatus.Alive, crab.Status));

        Assert.Empty(fixture.Vouchers);
        fixture.VoucherRepository.Verify(
            repo => repo.AddAsync(
                It.IsAny<HarvestVoucher>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitResults_RejectsContinueRaisingAfter45DaysWithoutMolt()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        fixture.Crabs[0].StockedAt = DateTime.UtcNow.AddDays(-46);

        var request = new SubmitHarvestAssignmentResultsRequest(
        [
            new HarvestAssignmentResultRequest(
                AssignmentLineId: fixture.Lines[0].Id,
                Outcome: HarvestAssignmentOutcome.ContinueRaising,
                Reason: "Chưa lột",
                MoltConfirmed: false)
        ]);

        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.SubmitAssignmentResultsAsync(
                fixture.Assignment.Id,
                request));

        Assert.Null(fixture.Lines[0].Outcome);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(1)]
    public async Task SubmitResults_RejectsHarvestOutsideStockingWindow(int days)
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        var request = new SubmitHarvestAssignmentResultsRequest(
        [
            new HarvestAssignmentResultRequest(
                AssignmentLineId: fixture.Lines[0].Id,
                Outcome: HarvestAssignmentOutcome.Harvested,
                WeightGram: 200, CarapaceWidthMm: 80, CarapaceLengthMm: 60,
                HarvestedAt: DateTimeOffset.UtcNow.AddDays(days))
        ]);
        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.SubmitAssignmentResultsAsync(fixture.Assignment.Id, request));
        Assert.Null(fixture.Lines[0].Outcome);
        Assert.Empty(fixture.Vouchers);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAssignment_RejectsDeferredCrabWithChangedLocation()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.ContinueRaising;
        fixture.Lines[0].EligibilityReason = "Chưa lột";
        fixture.Lines[0].BoxId = Guid.NewGuid();
        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.CompleteAssignmentAsync(fixture.Assignment.Id, new CompleteHarvestAssignmentRequest()));
        Assert.Equal(HarvestAssignmentStatus.InProgress, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAssignment_RejectsDeferredTerminalCrab()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.ContinueRaising;
        fixture.Lines[0].EligibilityReason = "Chưa lột";
        fixture.Crabs[0].Status = CrabStatus.Dead;
        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.CompleteAssignmentAsync(fixture.Assignment.Id, new CompleteHarvestAssignmentRequest()));
        Assert.Equal(HarvestAssignmentStatus.InProgress, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        fixture.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompleteAssignment_RejectsTaskWithUnprocessedCrabs()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 2);
        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.ContinueRaising;
        fixture.Lines[0].EligibilityReason = "Chưa đủ kích thước";

        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.CompleteAssignmentAsync(
                fixture.Assignment.Id,
                new CompleteHarvestAssignmentRequest()));

        Assert.Equal(HarvestAssignmentStatus.InProgress, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        fixture.UnitOfWork.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteAssignment_RejectsDeferredCrabThatPassed45DaysWithoutMolt()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        fixture.Crabs[0].StockedAt = DateTime.UtcNow.AddDays(-46);

        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.ContinueRaising;
        fixture.Lines[0].EligibilityReason = "Chưa lột";
        fixture.Lines[0].PendingMoltConfirmed = false;

        await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.CompleteAssignmentAsync(
                fixture.Assignment.Id,
                new CompleteHarvestAssignmentRequest()));

        Assert.Equal(
            HarvestAssignmentStatus.InProgress,
            fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
    }

    [Fact]
    public async Task CompleteAssignment_DeferredWithoutMolt_LeavesCrabAliveWithoutMoltRecord()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        var crab = fixture.Crabs[0];
        var line = fixture.Lines[0];

        line.Outcome = HarvestAssignmentOutcome.ContinueRaising;
        line.EligibilityReason = "Chưa đủ kích thước";
        line.PendingMoltConfirmed = false;

        await fixture.Service.CompleteAssignmentAsync(
            fixture.Assignment.Id,
            new CompleteHarvestAssignmentRequest());
        Assert.Equal(HarvestAssignmentStatus.PendingReview, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        Assert.All(fixture.Crabs, c => Assert.Equal(CrabStatus.Alive, c.Status));
        fixture.UseManager();
        await fixture.Service.ConfirmAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest());


        Assert.Equal(CrabStatus.Alive, crab.Status);
        Assert.Null(crab.MoltedAt);
        Assert.Empty(fixture.MoltingRecords);
        Assert.Equal(HarvestAssignmentLineStatus.Deferred, line.Status);
        Assert.Equal(HarvestAssignmentStatus.Completed, fixture.Assignment.Status);
    }

    [Fact]
    public async Task CompleteAssignment_DeferredWithConfirmedMolt_RecordsMoltAndLeavesCrabAlive()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        var crab = fixture.Crabs[0];
        var line = fixture.Lines[0];

        line.Outcome = HarvestAssignmentOutcome.ContinueRaising;
        line.EligibilityReason = "Đã lột, tiếp tục nuôi";
        line.PendingMoltConfirmed = true;
        line.CheckedAt = DateTime.UtcNow;

        await fixture.Service.CompleteAssignmentAsync(
            fixture.Assignment.Id,
            new CompleteHarvestAssignmentRequest());
        Assert.Equal(HarvestAssignmentStatus.PendingReview, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        Assert.All(fixture.Crabs, c => Assert.Equal(CrabStatus.Alive, c.Status));
        fixture.UseManager();
        await fixture.Service.ConfirmAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest());


        Assert.Equal(CrabStatus.Alive, crab.Status);
        Assert.Equal(CrabCondition.Softshell, crab.Condition);
        Assert.NotNull(crab.MoltedAt);

        var moltRecord = Assert.Single(fixture.MoltingRecords);
        Assert.Equal(crab.Id, moltRecord.CrabId);
        Assert.Equal("success", moltRecord.Result);
        Assert.Equal("harvest-assignment", moltRecord.Source);

        Assert.Equal(HarvestAssignmentLineStatus.Deferred, line.Status);
        Assert.Equal(HarvestAssignmentStatus.Completed, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
    }

    [Fact]
    public async Task CompleteAssignment_CreatesVoucherAndHarvestsCrabAfterConfirmedMolt()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 1);
        var crab = fixture.Crabs[0];
        var line = fixture.Lines[0];

        line.Outcome = HarvestAssignmentOutcome.Harvested;
        line.CheckedAt = DateTime.UtcNow;
        line.PendingMoltConfirmed = true;
        line.HarvestWeightGram = 350;
        line.HarvestGrade = "M";
        line.HarvestIsSoftshell = true;
        line.HarvestCarapaceLengthMm = 72;
        line.HarvestCarapaceWidthMm = 85;
        line.PendingHarvestedAt = DateTime.UtcNow;

        await fixture.Service.CompleteAssignmentAsync(
            fixture.Assignment.Id,
            new CompleteHarvestAssignmentRequest("Thu hoạch sau khi lột"));
        Assert.Equal(HarvestAssignmentStatus.PendingReview, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        Assert.All(fixture.Crabs, c => Assert.Equal(CrabStatus.Alive, c.Status));
        fixture.UseManager();
        await fixture.Service.ConfirmAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest());


        Assert.Equal(CrabStatus.Harvested, crab.Status);
        Assert.Equal(HarvestAssignmentStatus.Completed, fixture.Assignment.Status);

        var moltRecord = Assert.Single(fixture.MoltingRecords);
        Assert.Equal(crab.Id, moltRecord.CrabId);
        Assert.Equal("success", moltRecord.Result);

        var voucher = Assert.Single(fixture.Vouchers);
        Assert.Equal(1, voucher.TotalQuantity);
        Assert.Equal(HarvestStatus.Completed, voucher.Status);
        Assert.Single(voucher.Lines);
    }

    [Fact]
    public async Task CompleteAssignment_CreatesOneVoucherForHarvestedCrabsAndDefersOthers()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, lineCount: 2);
        var harvestedCrab = fixture.Crabs[0];
        var deferredCrab = fixture.Crabs[1];

        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.Harvested;
        fixture.Lines[0].HarvestWeightGram = 350;
        fixture.Lines[0].HarvestGrade = "M";
        fixture.Lines[0].HarvestIsSoftshell = true;
        fixture.Lines[0].HarvestCarapaceLengthMm = 72;
        fixture.Lines[0].HarvestCarapaceWidthMm = 85;
        fixture.Lines[0].PendingHarvestedAt = DateTime.UtcNow;

        fixture.Lines[1].Outcome = HarvestAssignmentOutcome.ContinueRaising;
        fixture.Lines[1].EligibilityReason = "Chưa đủ kích thước";

        await fixture.Service.CompleteAssignmentAsync(
            fixture.Assignment.Id,
            new CompleteHarvestAssignmentRequest("Thu hoạch theo task"));
        Assert.Equal(HarvestAssignmentStatus.PendingReview, fixture.Assignment.Status);
        Assert.Empty(fixture.Vouchers);
        Assert.All(fixture.Crabs, c => Assert.Equal(CrabStatus.Alive, c.Status));
        fixture.UseManager();
        await fixture.Service.ConfirmAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest());


        Assert.Equal(HarvestAssignmentStatus.Completed, fixture.Assignment.Status);
        Assert.NotNull(fixture.Assignment.CompletedAt);

        var voucher = Assert.Single(fixture.Vouchers);
        Assert.Equal(HarvestStatus.Completed, voucher.Status);
        Assert.Equal(fixture.Assignment.Id, voucher.HarvestAssignmentId);
        Assert.Equal(1, voucher.TotalQuantity);

        var harvestLine = Assert.Single(voucher.Lines);
        Assert.Equal(harvestedCrab.Id, harvestLine.CrabId);
        Assert.Equal(350, harvestLine.WeightGram);

        Assert.Equal(CrabStatus.Harvested, harvestedCrab.Status);
        Assert.Equal(HarvestAssignmentLineStatus.Harvested, fixture.Lines[0].Status);
        Assert.Equal(CrabStatus.Alive, deferredCrab.Status);
        Assert.Equal(HarvestAssignmentLineStatus.Deferred, fixture.Lines[1].Status);
        Assert.Empty(fixture.MoltingRecords);
    }

    [Fact]
    public async Task Staff_cannot_confirm_and_manager_can_return_for_correction()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, 1);
        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.ContinueRaising;
        fixture.Lines[0].EligibilityReason = "Chưa lột";
        await fixture.Service.CompleteAssignmentAsync(fixture.Assignment.Id, new CompleteHarvestAssignmentRequest());
        await Assert.ThrowsAsync<AppException>(() => fixture.Service.ConfirmAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest()));
        fixture.UseManager();
        await fixture.Service.ReturnAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest("Kiểm tra lại"));
        Assert.Equal(HarvestAssignmentStatus.InProgress, fixture.Assignment.Status);
        Assert.Equal("Kiểm tra lại", fixture.Assignment.ReviewNotes);
        Assert.Equal(HarvestAssignmentLineStatus.InProgress, fixture.Lines[0].Status);
        Assert.Empty(fixture.Vouchers);
    }

    [Fact]
    public async Task CancelAssignment_RequiresManagerAndPreservesCrab()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, 1);
        await Assert.ThrowsAsync<AppException>(() => fixture.Service.CancelAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest("Hủy")));
        fixture.UseManager();
        await fixture.Service.CancelAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest("Không thực hiện"));
        Assert.Equal(HarvestAssignmentStatus.Cancelled, fixture.Assignment.Status);
        Assert.Equal(HarvestAssignmentLineStatus.Cancelled, fixture.Lines[0].Status);
        Assert.Equal(CrabStatus.Alive, fixture.Crabs[0].Status);
        Assert.NotNull(fixture.Crabs[0].BoxId);
        Assert.Empty(fixture.Vouchers);
        Assert.Contains("cancelled", fixture.Assignment.WorkflowAuditJson);
    }

    [Fact]
    public async Task CancelAssignment_RejectsRecordedOutcome()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, 1);
        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.Harvested;
        fixture.UseManager();
        await Assert.ThrowsAsync<AppException>(() => fixture.Service.CancelAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest("Hủy")));
        Assert.Equal(HarvestAssignmentStatus.InProgress, fixture.Assignment.Status);
        Assert.Equal(HarvestAssignmentLineStatus.InProgress, fixture.Lines[0].Status);
    }

    [Fact]
    public async Task SkipTerminalLine_ExcludesPendingHarvestFromVoucherAndKeepsEvidence()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.PendingReview, 1);
        fixture.Lines[0].Outcome = HarvestAssignmentOutcome.Harvested;
        fixture.Lines[0].HarvestWeightGram = 350;
        fixture.Crabs[0].Status = CrabStatus.Dead;
        fixture.Crabs[0].BoxId = null;
        fixture.UseManager();
        await fixture.Service.SkipAssignmentLineAsync(fixture.Assignment.Id, fixture.Lines[0].Id, new SkipHarvestAssignmentLineRequest("Đối soát tử vong"));
        await fixture.Service.ConfirmAssignmentAsync(fixture.Assignment.Id, new ReviewHarvestAssignmentRequest());
        Assert.Equal(HarvestAssignmentStatus.Completed, fixture.Assignment.Status);
        Assert.Equal(HarvestAssignmentLineStatus.Skipped, fixture.Lines[0].Status);
        Assert.Equal(HarvestAssignmentOutcome.Harvested, fixture.Lines[0].Outcome);
        Assert.Equal(350, fixture.Lines[0].HarvestWeightGram);
        Assert.Equal(CrabStatus.Dead, fixture.Crabs[0].Status);
        Assert.Empty(fixture.Vouchers);
        Assert.Contains("line-skipped", fixture.Assignment.WorkflowAuditJson);
    }

    [Fact]
    public async Task SkipLine_RejectsLiveCrabAtOriginalLocation()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, 1);
        fixture.UseManager();
        await Assert.ThrowsAsync<AppException>(() => fixture.Service.SkipAssignmentLineAsync(fixture.Assignment.Id, fixture.Lines[0].Id, new SkipHarvestAssignmentLineRequest("Bỏ qua")));
        Assert.Equal(HarvestAssignmentLineStatus.InProgress, fixture.Lines[0].Status);
    }

    [Fact]
    public async Task Reassign_RequiresScopedStaffAndResetsAcceptance()
    {
        var fixture = new Fixture(HarvestAssignmentStatus.InProgress, 1);
        var old = fixture.Assignment.AssignedToUserId;
        var replacement = fixture.AddReplacementStaff();
        fixture.UseManager();
        await Assert.ThrowsAsync<AppException>(() => fixture.Service.ReassignAssignmentAsync(fixture.Assignment.Id, new ReassignHarvestAssignmentRequest(Guid.NewGuid(), "Đổi ca")));
        await fixture.Service.ReassignAssignmentAsync(fixture.Assignment.Id, new ReassignHarvestAssignmentRequest(replacement, "Đổi ca"));
        Assert.NotEqual(old, fixture.Assignment.AssignedToUserId);
        Assert.Equal(replacement, fixture.Assignment.AssignedToUserId);
        Assert.Equal(HarvestAssignmentStatus.Assigned, fixture.Assignment.Status);
        Assert.Equal(HarvestAssignmentLineStatus.Assigned, fixture.Lines[0].Status);
        Assert.Null(fixture.Assignment.StartedAt);
    }

    private sealed class Fixture
    {
        private readonly Mock<ICurrentUserService> _currentUser = new();
        private readonly Guid _managerId = Guid.NewGuid();
        public void UseManager() => _currentUser.Setup(x => x.UserId).Returns(_managerId);
        public Guid AddReplacementStaff() => _replacementId;
        private readonly Guid _replacementId = Guid.NewGuid();

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IRepository<HarvestVoucher>> VoucherRepository { get; }
        public HarvestAssignmentService Service { get; }
        public HarvestAssignment Assignment { get; }
        public List<HarvestAssignmentLine> Lines { get; }
        public List<Crab> Crabs { get; }
        public List<HarvestVoucher> Vouchers { get; }
        public List<MoltingRecord> MoltingRecords { get; }

        public Fixture(HarvestAssignmentStatus status, int lineCount)
        {
            var staffId = Guid.NewGuid();
            var areaId = Guid.NewGuid();
            var rowId = Guid.NewGuid();

            _currentUser.Setup(x => x.UserId).Returns(staffId);
            _currentUser.Setup(x => x.IsAuthenticated).Returns(true);

            var staff = new AppUser
            {
                Id = staffId,
                Role = UserRole.FarmStaff,
                IsActive = true,
                FullName = "Test Staff"
            };

            Assignment = new HarvestAssignment
            {
                Id = Guid.NewGuid(),
                FarmingAreaId = areaId,
                AssignedToUserId = staffId,
                AssignedByUserId = _managerId,
                AssignmentCode = "HA-TEST",
                Status = status
            };

            Lines = Enumerable.Range(0, lineCount)
                .Select(_ => new HarvestAssignmentLine
                {
                    Id = Guid.NewGuid(),
                    HarvestAssignmentId = Assignment.Id,
                    CrabId = Guid.NewGuid(),
                    Status = status == HarvestAssignmentStatus.Assigned
                        ? HarvestAssignmentLineStatus.Assigned
                        : HarvestAssignmentLineStatus.InProgress
                })
                .ToList();

            Assignment.Lines = Lines;

            var boxes = new List<Box>();
            Crabs = new List<Crab>();

            foreach (var (line, index) in Lines.Select((line, index) => (line, index)))
            {
                var boxId = Guid.NewGuid();

                line.CrabCode = $"CRAB-{index + 1}";
                line.BoxId = boxId;
                line.BoxCode = $"BOX-{index + 1}";

                Crabs.Add(new Crab
                {
                    Id = line.CrabId,
                    Code = line.CrabCode,
                    Status = CrabStatus.Alive,
                    Condition = CrabCondition.Normal,
                    StockedAt = DateTime.UtcNow.AddDays(-10),
                    BoxId = boxId
                });

                boxes.Add(new Box
                {
                    Id = boxId,
                    Code = line.BoxCode!,
                    FarmingRowId = rowId,
                    Status = "Occupied",
                    IsOccupied = true
                });
            }

            var rows = new List<FarmingRow>
            {
                new()
                {
                    Id = rowId,
                    FarmingAreaId = areaId,
                    Code = "ROW-1",
                    Name = "Row 1"
                }
            };

            Vouchers = new List<HarvestVoucher>();
            MoltingRecords = new List<MoltingRecord>();

            var harvestLines = new List<HarvestLine>();
            var harvestHistories = new List<CrabHarvestHistory>();
            var crabStatusHistories = new List<CrabStatusHistory>();
            var boxStatusHistories = new List<BoxStatusHistory>();

            UnitOfWork.Setup(x => x.Users)
                .Returns(CreateRepository([staff, new AppUser { Id = _managerId, Role = UserRole.FarmManager, IsActive = true, FullName = "Test Manager" }, new AppUser { Id = _replacementId, Role = UserRole.FarmStaff, IsActive = true, FullName = "Replacement" }]).Object);
            UnitOfWork.Setup(x => x.FarmManagerAssignments)
                .Returns(CreateRepository([new FarmManagerAssignment { ManagerUserId = _managerId, FarmingAreaId = areaId, AssignedByUserId = _managerId }]).Object);
            UnitOfWork.Setup(x => x.FarmStaffAssignments)
                .Returns(CreateRepository(
                [
                    new FarmStaffAssignment
                    {
                        Id = Guid.NewGuid(),
                        StaffUserId = staffId,
                        FarmingAreaId = areaId
                    },
                    new FarmStaffAssignment { StaffUserId = _replacementId, ManagerUserId = _managerId, FarmingAreaId = areaId, AssignedByUserId = _managerId }
                ]).Object);
            UnitOfWork.Setup(x => x.HarvestAssignments)
                .Returns(CreateRepository([Assignment]).Object);
            UnitOfWork.Setup(x => x.HarvestAssignmentLines)
                .Returns(CreateRepository(Lines).Object);
            UnitOfWork.Setup(x => x.Crabs)
                .Returns(CreateRepository(Crabs).Object);
            UnitOfWork.Setup(x => x.Boxes)
                .Returns(CreateRepository(boxes).Object);
            UnitOfWork.Setup(x => x.FarmingRows)
                .Returns(CreateRepository(rows).Object);
            UnitOfWork.Setup(x => x.FarmingAreas)
                .Returns(CreateRepository(
                [
                    new FarmingArea
                    {
                        Id = areaId,
                        Code = "AREA-1",
                        Name = "Area 1"
                    }
                ]).Object);

            UnitOfWork.Setup(x => x.CrabHarvestHistories)
                .Returns(CreateRepository(harvestHistories).Object);
            UnitOfWork.Setup(x => x.CrabStatusHistories)
                .Returns(CreateRepository(crabStatusHistories).Object);
            UnitOfWork.Setup(x => x.MoltingRecords)
                .Returns(CreateRepository(MoltingRecords).Object);
            UnitOfWork.Setup(x => x.BoxStatusHistories)
                .Returns(CreateRepository(boxStatusHistories).Object);
            UnitOfWork.Setup(x => x.CrabBoxAllocations)
                .Returns(CreateRepository(new List<CrabBoxAllocation>()).Object);
            UnitOfWork.Setup(x => x.HarvestLines)
                .Returns(CreateRepository(harvestLines).Object);

            VoucherRepository = CreateRepository(Vouchers);
            UnitOfWork.Setup(x => x.HarvestVouchers)
                .Returns(VoucherRepository.Object);

            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1);

            Service = new HarvestAssignmentService(
                UnitOfWork.Object,
                _currentUser.Object,
                new HarvestRecorder(UnitOfWork.Object, _currentUser.Object));
        }
    }

    private static Mock<IRepository<T>> CreateRepository<T>(List<T> items)
        where T : BaseEntity
    {
        var repository = new Mock<IRepository<T>>();

        repository.Setup(x => x.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                items.FirstOrDefault(item => item.Id == id));

        repository.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IEnumerable<T>)items.ToList());

        repository.Setup(x => x.FindAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.Where(predicate.Compile()).ToList());

        repository.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.FirstOrDefault(predicate.Compile()));

        repository.Setup(x => x.AnyAsync(
                It.IsAny<Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate, CancellationToken _) =>
                items.Any(predicate.Compile()));

        repository.Setup(x => x.AddAsync(
                It.IsAny<T>(),
                It.IsAny<CancellationToken>()))
            .Callback<T, CancellationToken>((item, _) => items.Add(item))
            .Returns(Task.CompletedTask);

        repository.Setup(x => x.Update(It.IsAny<T>()));

        repository.Setup(x => x.Remove(It.IsAny<T>()))
            .Callback<T>(item => items.Remove(item));

        repository.Setup(x => x.CountAsync(
                It.IsAny<Expression<Func<T, bool>>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<T, bool>>? predicate, CancellationToken _) =>
                predicate is null
                    ? items.Count
                    : items.Count(predicate.Compile()));

        repository.Setup(x => x.Query())
            .Returns(() => items.AsQueryable());

        return repository;
    }
}