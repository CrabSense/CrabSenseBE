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
                    MoltingStage = "pre-molt"
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

    private void StubEmptyCrabsAndAlerts()
    {
        var crabRepo = new Mock<IRepository<Crab>>();
        crabRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Crab>());
        var alertRepo = new Mock<IRepository<Alert>>();
        alertRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Alert>());
        _uow.Setup(u => u.Crabs).Returns(crabRepo.Object);
        _uow.Setup(u => u.Alerts).Returns(alertRepo.Object);
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
