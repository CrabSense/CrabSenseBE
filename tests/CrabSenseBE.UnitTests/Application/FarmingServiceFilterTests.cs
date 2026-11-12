using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: filter khu/dãy/box + CRUD guards.</summary>
public class FarmingServiceFilterTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<CrabSenseBE.Application.Interfaces.IBoxQrService> _qr = new();
    private readonly Mock<IPublicImageStorage> _images = new();

    private FarmingService Create() => new(_uow.Object, _qr.Object, _images.Object);

    [Fact]
    public async Task GetAreas_FiltersBySearchAndActive()
    {
        var areas = new[]
        {
            new FarmingArea { Id = Guid.NewGuid(), Name = "Khu A", IsActive = true },
            new FarmingArea { Id = Guid.NewGuid(), Name = "Khu B", IsActive = false },
            new FarmingArea { Id = Guid.NewGuid(), Name = "Zone C", IsActive = true }
        };
        var repo = new Mock<IRepository<FarmingArea>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(areas);
        _uow.Setup(u => u.FarmingAreas).Returns(repo.Object);
        var userRepo = new Mock<IRepository<AppUser>>();
        userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AppUser>());
        _uow.Setup(u => u.Users).Returns(userRepo.Object);
        StubEmptyAreaStatsDeps();

        var result = await Create().GetAreasAsync(new FarmingAreaFilter("Khu", true, null, 1, 50));
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.First().Name.Should().Be("Khu A");
    }

    [Fact]
    public async Task GetBoxes_FiltersByStatusAndCode()
    {
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxes = new[]
        {
            new Box { Id = Guid.NewGuid(), FarmingRowId = rowId, Code = "A01", Status = "active", IsOccupied = true },
            new Box { Id = Guid.NewGuid(), FarmingRowId = rowId, Code = "A02", Status = "empty", IsOccupied = false },
            new Box { Id = Guid.NewGuid(), FarmingRowId = rowId, Code = "B01", Status = "active", IsOccupied = true }
        };
        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(boxes);
        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new FarmingRow { Id = rowId, FarmingAreaId = areaId, Name = "R1" } });
        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new FarmingArea { Id = areaId, Name = "A1" } });

        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        StubEmptyCrabsAndAlerts();

        var result = await Create().GetBoxesAsync(new BoxFilter(null, rowId, "A01", "active", true, 1, 50));
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.First().Code.Should().Be("A01");
        result.Data.Items.First().FarmingAreaId.Should().Be(areaId);
    }

    [Fact]
    public async Task GetBoxes_MatchesCrabTagAndBuildsDisplayName()
    {
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var boxes = new[]
        {
            new Box { Id = boxId, FarmingRowId = rowId, Code = "BOX-0001", Status = "active", IsOccupied = true }
        };
        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(boxes);
        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmingRow { Id = rowId, FarmingAreaId = areaId, Name = "Dãy 01", Code = "DAY-A01" }
            });
        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmingArea { Id = areaId, Name = "Khu A", Code = "AREA-A01" }
            });
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Crab
                {
                    Id = Guid.NewGuid(),
                    BoxId = boxId,
                    Tag = "CRAB-001",
                    Status = CrabSenseBE.Domain.Enums.CrabStatus.Alive,
                    MoltingStage = "pre-molt",
                    Condition = CrabSenseBE.Domain.Enums.CrabCondition.Premolt
                }
            });
        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());

        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);

        var result = await Create().GetBoxesAsync(new BoxFilter(Code: "CRAB-001"));
        result.Data!.TotalCount.Should().Be(1);
        var dto = result.Data.Items.First();
        dto.DisplayName.Should().Be("Hộp A-001");
        dto.CrabTag.Should().Be("CRAB-001");
        dto.CrabCondition.Should().Be("premolt");
        dto.AiSummary.Should().Be("Sắp lột — tăng theo dõi");
    }

    [Fact]
    public async Task GetBoxes_DoesNotTreatDeadCrabAsOccupied()
    {
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var boxes = new[]
        {
            new Box { Id = boxId, FarmingRowId = rowId, Code = "BOX-0002", Status = "active", IsOccupied = true }
        };
        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(boxes);
        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmingRow { Id = rowId, FarmingAreaId = areaId, Name = "Dãy 01", Code = "DAY-A01" }
            });
        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new FarmingArea { Id = areaId, Name = "Khu A", Code = "AREA-A01" }
            });
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new Crab
                {
                    Id = Guid.NewGuid(),
                    BoxId = boxId,
                    Tag = "CRAB-DEAD",
                    Status = CrabSenseBE.Domain.Enums.CrabStatus.Dead,
                    Condition = CrabSenseBE.Domain.Enums.CrabCondition.Dead
                }
            });
        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());

        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);

        var result = await Create().GetBoxesAsync(new BoxFilter());
        var dto = result.Data!.Items.Should().ContainSingle().Subject;
        dto.IsOccupied.Should().BeFalse();
        dto.Status.Should().Be("empty");
        dto.CrabId.Should().BeNull();
        dto.CrabTag.Should().BeNull();
    }

    [Fact]
    public async Task BoxStatus_cannot_mark_allocated_box_empty()
    {
        var box = new Box { Id = Guid.NewGuid(), FarmingRowId = Guid.NewGuid(), Code = "A01", Status = "active", IsOccupied = true };
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(box.Id, It.IsAny<CancellationToken>())).ReturnsAsync(box);
        var rows = new Mock<IRepository<FarmingRow>>();
        rows.Setup(r => r.GetByIdAsync(box.FarmingRowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingRow { Id = box.FarmingRowId, FarmingAreaId = Guid.NewGuid() });
        var allocations = new Mock<IRepository<CrabBoxAllocation>>();
        allocations.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _uow.Setup(x => x.Boxes).Returns(boxes.Object);
        _uow.Setup(x => x.FarmingRows).Returns(rows.Object);
        _uow.Setup(x => x.CrabBoxAllocations).Returns(allocations.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => Create().UpdateBoxStatusAsync(box.Id, new UpdateBoxStatusRequest("empty", false)));
        ex.StatusCode.Should().Be(409);
        box.IsOccupied.Should().BeTrue();
        box.Status.Should().Be("active");
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task CrabProfile_rejects_nonpositive_weight(int weight)
    {
        var crab = new Crab { Id = Guid.NewGuid(), WeightGram = 200 };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        _uow.Setup(x => x.Crabs).Returns(crabs.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => Create().UpdateCrabAsync(crab.Id, new UpdateCrabRequest(null, weight)));
        ex.StatusCode.Should().Be(400);
        crab.WeightGram.Should().Be(200);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CrabProfile_cannot_clear_quarantine_with_normal_condition()
    {
        var crab = new Crab { Id = Guid.NewGuid(), Status = CrabSenseBE.Domain.Enums.CrabStatus.Quarantined, Condition = CrabSenseBE.Domain.Enums.CrabCondition.Problem };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        _uow.Setup(x => x.Crabs).Returns(crabs.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => Create().UpdateCrabAsync(crab.Id, new UpdateCrabRequest(null, null, Condition: "normal")));
        ex.StatusCode.Should().Be(409);
        crab.Status.Should().Be(CrabSenseBE.Domain.Enums.CrabStatus.Quarantined);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Historical_weight_is_recorded_without_overwriting_latest_snapshot()
    {
        var now = DateTime.UtcNow;
        var crab = new Crab { Id = Guid.NewGuid(), WeightGram = 250, StockedAt = now.AddDays(-10),
            CarapaceWidthMm = 80, CarapaceLengthMm = 60 };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        var weights = new Mock<IRepository<CrabWeightHistory>>();
        weights.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabWeightHistory, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new CrabWeightHistory { CrabId = crab.Id, WeightGram = 250, MeasuredAt = now.AddDays(-1) } });
        CrabWeightHistory? saved = null;
        weights.Setup(r => r.AddAsync(It.IsAny<CrabWeightHistory>(), It.IsAny<CancellationToken>()))
            .Callback<CrabWeightHistory, CancellationToken>((x, _) => saved = x).Returns(Task.CompletedTask);
        _uow.Setup(x => x.Crabs).Returns(crabs.Object);
        _uow.Setup(x => x.CrabWeightHistories).Returns(weights.Object);

        await Create().RecordCrabWeightAsync(crab.Id,
            new RecordCrabWeightRequest(now.AddDays(-2), 200, 70, 50, Source: "ai", RecordedByName: "fake"));
        saved.Should().NotBeNull();
        saved!.WeightGram.Should().Be(200);
        saved.Source.Should().Be("manual");
        saved.RecordedByName.Should().BeNull();
        crab.WeightGram.Should().Be(250);
        crab.CarapaceWidthMm.Should().Be(80);
        crab.CarapaceLengthMm.Should().Be(60);
        crabs.Verify(r => r.Update(It.IsAny<Crab>()), Times.Never);
    }

    [Fact]
    public async Task Weight_measurement_before_stocking_is_rejected()
    {
        var crab = new Crab { Id = Guid.NewGuid(), WeightGram = 200, StockedAt = DateTime.UtcNow.AddDays(-1) };
        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.GetByIdAsync(crab.Id, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        _uow.Setup(x => x.Crabs).Returns(crabs.Object);
        var ex = await Assert.ThrowsAsync<AppException>(() => Create().RecordCrabWeightAsync(crab.Id,
            new RecordCrabWeightRequest(DateTime.UtcNow.AddDays(-2), 150)));
        ex.StatusCode.Should().Be(400);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private void StubEmptyCrabsAndAlerts()
    {
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Crab>());
        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());
        alertRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<Alert, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
        var allocRepo = new Mock<IRepository<CrabBoxAllocation>>();
        allocRepo.Setup(r => r.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CrabBoxAllocation>());
        _uow.Setup(u => u.CrabBoxAllocations).Returns(allocRepo.Object);
    }

    private void StubEmptyAreaStatsDeps()
    {
        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FarmingRow>());
        var boxRepo = new Mock<IRepository<Box>>();
        boxRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Box>());
        var wsRepo = new Mock<IRepository<WaterSystem>>();
        wsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WaterSystem>());
        var sensorRepo = new Mock<IRepository<Sensor>>();
        sensorRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Sensor>());

        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);
        _uow.Setup(u => u.Boxes).Returns(boxRepo.Object);
        _uow.Setup(u => u.WaterSystems).Returns(wsRepo.Object);
        _uow.Setup(u => u.Sensors).Returns(sensorRepo.Object);
        StubEmptyCrabsAndAlerts();
    }

    [Fact]
    public async Task DeleteArea_WhenHasRows_ThrowsConflict()
    {
        var id = Guid.NewGuid();
        var areaRepo = new Mock<IRepository<FarmingArea>>();
        areaRepo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FarmingArea { Id = id, Name = "A" });
        var rowRepo = new Mock<IRepository<FarmingRow>>();
        rowRepo.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<FarmingRow, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _uow.Setup(u => u.FarmingAreas).Returns(areaRepo.Object);
        _uow.Setup(u => u.FarmingRows).Returns(rowRepo.Object);

        var ex = await Assert.ThrowsAsync<AppException>(() => Create().DeleteAreaAsync(id));
        ex.StatusCode.Should().Be(409);
    }
}
