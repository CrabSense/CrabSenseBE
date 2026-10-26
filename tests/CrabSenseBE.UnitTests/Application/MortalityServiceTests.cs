using System.Text.Json;
using CrabSenseBE.Application.DTOs.Mortality;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>
/// Cua chết phải lưu snapshot lúc chết (cân, kích thước mai, hộp),
/// vì sau đó cua bị gỡ khỏi hộp và dữ liệu cân có thể bị sửa.
/// </summary>
public class MortalityServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IRepository<Crab>> _crabs = new();
    private readonly Mock<IRepository<CrabMortalityRecord>> _records = new();
    private readonly Mock<IRepository<CrabBoxAllocation>> _allocs = new();
    private readonly Mock<IRepository<CrabStatusHistory>> _statusHistories = new();
    private readonly Mock<IRepository<Box>> _boxes = new();
    private readonly Mock<IRepository<BoxStatusHistory>> _boxHistories = new();
    private readonly Mock<IRepository<FarmingRow>> _rows = new();
    private readonly Mock<IRepository<FarmingArea>> _areas = new();

    private CrabMortalityRecord? _saved;

    public MortalityServiceTests()
    {
        _records.Setup(r => r.AddAsync(It.IsAny<CrabMortalityRecord>(), It.IsAny<CancellationToken>()))
            .Callback<CrabMortalityRecord, CancellationToken>((r, _) => _saved = r)
            .Returns(Task.CompletedTask);
        _allocs.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CrabBoxAllocation, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CrabBoxAllocation>());
        _crabs.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Crab, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Crab>());
        _statusHistories.Setup(r => r.AddAsync(It.IsAny<CrabStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _boxHistories.Setup(r => r.AddAsync(It.IsAny<BoxStatusHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _rows.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) => null);
        _areas.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) => null);
        _uow.Setup(u => u.Crabs).Returns(_crabs.Object);
        _uow.Setup(u => u.CrabMortalityRecords).Returns(_records.Object);
        _uow.Setup(u => u.CrabBoxAllocations).Returns(_allocs.Object);
        _uow.Setup(u => u.CrabStatusHistories).Returns(_statusHistories.Object);
        _uow.Setup(u => u.Boxes).Returns(_boxes.Object);
        _uow.Setup(u => u.BoxStatusHistories).Returns(_boxHistories.Object);
        _uow.Setup(u => u.FarmingRows).Returns(_rows.Object);
        _uow.Setup(u => u.FarmingAreas).Returns(_areas.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private MortalityService Create() => new(_uow.Object);

    private Crab GivenCrab(decimal? weight = 85m, decimal? width = 62m, decimal? length = 55m)
    {
        var boxId = Guid.NewGuid();
        var crab = new Crab
        {
            Id = Guid.NewGuid(),
            Code = "CRAB-0001",
            Tag = "A1",
            Status = CrabStatus.Alive,
            Condition = CrabCondition.Normal,
            BoxId = boxId,
            WeightGram = weight,
            CarapaceWidthMm = width,
            CarapaceLengthMm = length,
            BoxAllocations = new List<CrabBoxAllocation>()
        };
        _uow.Setup(u => u.GetCrabWithDetailsAsync(crab.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crab);
        _boxes.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Box { Id = boxId, Code = "BOX-0001", FarmingRowId = Guid.NewGuid() });
        return crab;
    }

    [Fact]
    public async Task RecordsWeightAtDeath_FromRequest()
    {
        var crab = GivenCrab();

        var dto = await Create().RecordMortalityAsync(
            Guid.NewGuid(),
            new RecordMortalityRequest(crab.Id, null, MortalityCause.WaterQuality, "nước đục", 70.5m));

        dto.WeightGram.Should().Be(70.5m);
        _saved!.WeightGram.Should().Be(70.5m);
        _saved.CarapaceWidthMm.Should().Be(62m, "không gửi thì lấy số đo hiện tại của cua");
        crab.WeightGram.Should().Be(70.5m, "cân mới nhất là cân lúc chết");
    }

    [Fact]
    public async Task FallsBackToCurrentCrabMeasurements_WhenClientOmitsThem()
    {
        var crab = GivenCrab(weight: 92.25m, width: 64m, length: 58m);

        var dto = await Create().RecordMortalityAsync(
            Guid.NewGuid(),
            new RecordMortalityRequest(crab.Id, null, MortalityCause.Unknown, null));

        dto.WeightGram.Should().Be(92.25m);
        dto.CarapaceWidthMm.Should().Be(64m);
        dto.CarapaceLengthMm.Should().Be(58m);
    }

    [Fact]
    public async Task SnapshotsBox_BeforeReleasingIt()
    {
        var crab = GivenCrab();

        var dto = await Create().RecordMortalityAsync(
            Guid.NewGuid(),
            new RecordMortalityRequest(crab.Id, null, MortalityCause.Disease, null));

        _saved!.BoxId.Should().NotBeNull();
        dto.BoxId.Should().Be(_saved.BoxId!.Value);
        dto.BoxCode.Should().Be("BOX-0001");
        crab.BoxId.Should().BeNull("cua chết phải được gỡ khỏi hộp");
    }

    [Fact]
    public async Task RejectsCrabThatIsAlreadyDead()
    {
        var crab = GivenCrab();
        crab.Status = CrabStatus.Dead;

        var act = () => Create().RecordMortalityAsync(
            Guid.NewGuid(),
            new RecordMortalityRequest(crab.Id, null, MortalityCause.Unknown, null));

        await act.Should().ThrowAsync<CrabSenseBE.Application.Common.AppException>();
    }

    [Fact]
    public void CauseAcceptsStringAndNumber()
    {
        // Cấu hình giống Program.cs: camelCase.
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var crabId = Guid.NewGuid();

        var fromString = JsonSerializer.Deserialize<RecordMortalityRequest>(
            $$"""{"crabId":"{{crabId}}","cause":"WaterQuality"}""", options);
        var fromNumber = JsonSerializer.Deserialize<RecordMortalityRequest>(
            $$"""{"crabId":"{{crabId}}","cause":2}""", options);

        fromString!.Cause.Should().Be(MortalityCause.WaterQuality);
        fromNumber!.Cause.Should().Be(MortalityCause.WaterQuality);
    }

    [Fact]
    public async Task ListReturnsBoxRowAreaNames()
    {
        var areaId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var boxId = Guid.NewGuid();
        var crabId = Guid.NewGuid();

        _uow.Setup(u => u.GetMortalityRecordsWithDetailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CrabMortalityRecord>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    CrabId = crabId,
                    Crab = new Crab { Id = crabId, Tag = "A1" },
                    BoxId = boxId,
                    Box = new Box
                    {
                        Id = boxId,
                        Code = "BOX-0001",
                        FarmingRowId = rowId,
                        FarmingRow = new FarmingRow
                        {
                            Id = rowId,
                            Name = "Dãy A",
                            FarmingAreaId = areaId,
                            FarmingArea = new FarmingArea { Id = areaId, Name = "Khu 1" }
                        }
                    },
                    MortalityDate = DateTime.UtcNow,
                    Cause = MortalityCause.Feeding,
                    WeightGram = 80m,
                    RecordedBy = Guid.NewGuid()
                }
            });

        var list = await Create().GetMortalityRecordsAsync();

        var row = list.Should().ContainSingle().Subject;
        row.BoxCode.Should().Be("BOX-0001");
        row.RowName.Should().Be("Dãy A");
        row.AreaName.Should().Be("Khu 1");
        row.WeightGram.Should().Be(80m);
    }
}
