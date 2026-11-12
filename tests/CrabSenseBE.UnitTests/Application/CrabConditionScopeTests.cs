using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Condition;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public sealed class CrabConditionScopeTests
{
    [Fact]
    public async Task Staff_cannot_submit_condition_outside_assigned_area()
    {
        var staffId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowB = Guid.NewGuid();
        var boxB = Guid.NewGuid();
        var (uow, current) = ScopeFixture(staffId, areaA);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(boxB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxB, FarmingRowId = rowB });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(rowB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = rowB, FarmingAreaId = areaB });
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        var service = new CrabConditionService(uow.Object, current.Object);

        var act = () => service.SubmitAsync(new SubmitCrabConditionRequest(
            boxB, 200, CarapaceWidthCm: 8));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(403);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Staff_cannot_use_crab_id_from_another_box()
    {
        var staffId = Guid.NewGuid();
        var area = Guid.NewGuid();
        var row = Guid.NewGuid();
        var requestedBox = Guid.NewGuid();
        var crabBox = Guid.NewGuid();
        var crabId = Guid.NewGuid();
        var (uow, current) = ScopeFixture(staffId, area);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(repo => repo.GetByIdAsync(requestedBox, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = requestedBox, FarmingRowId = row });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(repo => repo.GetByIdAsync(row, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = row, FarmingAreaId = area });
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(repo => repo.GetByIdAsync(crabId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Crab
            {
                Id = crabId,
                BoxId = crabBox,
                Status = CrabStatus.Alive
            });
        uow.Setup(unit => unit.Boxes).Returns(boxes.Object);
        uow.Setup(unit => unit.FarmingRows).Returns(rows.Object);
        uow.Setup(unit => unit.Crabs).Returns(crabs.Object);
        var service = new CrabConditionService(uow.Object, current.Object);

        var act = () => service.SubmitAsync(new SubmitCrabConditionRequest(
            requestedBox, 200, CarapaceWidthCm: 8, CrabId: crabId));

        var error = await act.Should().ThrowAsync<AppException>();
        error.Which.StatusCode.Should().Be(400);
        uow.Verify(
            unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Condition_measurement_preserves_quarantine_and_uses_authenticated_inspector()
    {
        var staffId = Guid.NewGuid();
        var area = Guid.NewGuid();
        var row = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var crabId = Guid.NewGuid();
        var (uow, current) = ScopeFixture(staffId, area);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxId, FarmingRowId = row });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.GetByIdAsync(row, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = row, FarmingAreaId = area });
        var crab = new Crab { Id = crabId, BoxId = boxId, Status = CrabStatus.Quarantined,
            Condition = CrabCondition.Problem, MoltingStage = "hard-shell" };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        var inspections = new Mock<IRepository<Inspection>>();
        Inspection? saved = null;
        inspections.Setup(r => r.AddAsync(It.IsAny<Inspection>(), It.IsAny<CancellationToken>()))
            .Callback<Inspection, CancellationToken>((x, _) => saved = x).Returns(Task.CompletedTask);
        uow.Setup(x => x.Boxes).Returns(boxes.Object);
        uow.Setup(x => x.FarmingRows).Returns(rows.Object);
        uow.Setup(x => x.Crabs).Returns(crabs.Object);
        uow.Setup(x => x.Inspections).Returns(inspections.Object);
        uow.Setup(x => x.CrabWeightHistories).Returns(Mock.Of<IRepository<CrabWeightHistory>>());
        uow.Setup(x => x.CrabAiAnalyses).Returns(Mock.Of<IRepository<CrabAiAnalysis>>());
        uow.Setup(x => x.AiRecommendations).Returns(Mock.Of<IRepository<AiRecommendation>>());
        var service = new CrabConditionService(uow.Object, current.Object);
        await service.SubmitAsync(new SubmitCrabConditionRequest(boxId, 243m, CarapaceWidthCm: 7.5m,
            CrabType: "duc", CrabId: crabId, OperatorId: Guid.NewGuid(), OperatorName: "spoofed"));
        crab.Status.Should().Be(CrabStatus.Quarantined);
        crab.Condition.Should().Be(CrabCondition.Problem);
        crab.MoltingStage.Should().Be("hard-shell");
        crab.WeightGram.Should().Be(243m);
        saved.Should().NotBeNull();
        saved!.InspectorId.Should().Be(staffId);
        saved.OperatorName.Should().Be("staff");
    }

    [Fact]
    public async Task Condition_rejects_photo_from_another_box_before_saving()
    {
        var staffId = Guid.NewGuid();
        var area = Guid.NewGuid();
        var row = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var (uow, current) = ScopeFixture(staffId, area);
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxId, FarmingRowId = row });
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.GetByIdAsync(row, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = row, FarmingAreaId = area });
        var media = new Mock<IRepository<MediaAsset>>();
        media.Setup(r => r.GetByIdAsync(photoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaAsset { Id = photoId, BoxId = Guid.NewGuid(), Category = "image" });
        uow.Setup(x => x.Boxes).Returns(boxes.Object);
        uow.Setup(x => x.FarmingRows).Returns(rows.Object);
        uow.Setup(x => x.MediaAssets).Returns(media.Object);
        var service = new CrabConditionService(uow.Object, current.Object);
        Func<Task> act = () => service.SubmitAsync(new SubmitCrabConditionRequest(boxId, 200m,
            CarapaceWidthCm: 8m, PhotoUrls: new[] { $"/api/media/{photoId}/content" }));
        (await act.Should().ThrowAsync<AppException>()).Which.StatusCode.Should().Be(403);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (
        Mock<IUnitOfWork> Uow,
        Mock<ICurrentUserService> Current) ScopeFixture(
        Guid staffId,
        Guid areaId)
    {
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(service => service.IsAuthenticated).Returns(true);
        current.SetupGet(service => service.UserId).Returns(staffId);
        var users = new Mock<IRepository<AppUser>>();
        users.Setup(repo => repo.GetByIdAsync(
                staffId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppUser
            {
                Id = staffId,
                Username = "staff",
                Role = UserRole.FarmStaff,
                IsActive = true
            });
        var assignments = new Mock<IRepository<FarmStaffAssignment>>();
        assignments.Setup(repo => repo.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<
                    Func<FarmStaffAssignment, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmStaffAssignment
            {
                StaffUserId = staffId,
                ManagerUserId = Guid.NewGuid(),
                FarmingAreaId = areaId
            });
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(unit => unit.Users).Returns(users.Object);
        uow.Setup(unit => unit.FarmStaffAssignments).Returns(assignments.Object);
        return (uow, current);
    }
}
