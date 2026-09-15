using System.Linq.Expressions;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>
/// Regression guard for POST /api/boxes/{id}/crabs (the mobile add-crab sheet):
/// <list type="bullet">
/// <item>the DTO used to omit Gender/CrabType/Condition/Notes/Carapace*, so the JSON binder
/// silently dropped every value the form collected;</item>
/// <item>the service never allocated Crab.Code, so the first crab stored Code="" and every
/// later add blew up on the unique index IX_Crabs_Code with 23505.</item>
/// </list>
/// </summary>
public class BoxDetailServiceAddCrabTests
{
    private static readonly Guid LotId = Guid.NewGuid();

    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IBoxQrService> _boxQr = new();

    /// <summary>Stand-in for the Crabs table so code allocation sees what earlier adds wrote.</summary>
    private readonly List<Crab> _table = [];

    private Crab? _saved;
    private QrCode? _savedQr;

    private BoxDetailService Create(Guid boxId)
    {
        var box = new Box { Id = boxId, Code = "A01", Status = "empty" };

        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>())).ReturnsAsync(box);

        var crabs = new Mock<IRepository<Crab>>();
        crabs.Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<Crab, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Crab, bool>> p, CancellationToken _) =>
                _table.Where(p.Compile()).ToList());
        crabs.Setup(r => r.AnyAsync(
                It.IsAny<Expression<Func<Crab, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<Crab, bool>> p, CancellationToken _) =>
                _table.Any(p.Compile()));
        crabs.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _table.ToList());
        crabs.Setup(r => r.AddAsync(It.IsAny<Crab>(), It.IsAny<CancellationToken>()))
            .Callback<Crab, CancellationToken>((c, _) =>
            {
                _saved = c;
                _table.Add(c);
            })
            .Returns(Task.CompletedTask);

        var lots = new Mock<IRepository<CrabLot>>();
        lots.Setup(r => r.GetByIdAsync(LotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CrabLot { Id = LotId });

        var allocations = new Mock<IRepository<CrabBoxAllocation>>();
        allocations.Setup(r => r.AddAsync(
                It.IsAny<CrabBoxAllocation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var qrCodes = new Mock<IRepository<QrCode>>();
        qrCodes.Setup(r => r.AddAsync(It.IsAny<QrCode>(), It.IsAny<CancellationToken>()))
            .Callback<QrCode, CancellationToken>((q, _) => _savedQr = q)
            .Returns(Task.CompletedTask);

        _uow.Setup(u => u.Boxes).Returns(boxes.Object);
        _uow.Setup(u => u.Crabs).Returns(crabs.Object);
        _uow.Setup(u => u.CrabLots).Returns(lots.Object);
        _uow.Setup(u => u.CrabBoxAllocations).Returns(allocations.Object);
        _uow.Setup(u => u.QrCodes).Returns(qrCodes.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return new BoxDetailService(_uow.Object, _boxQr.Object);
    }

    private static MobileAddCrabRequest Minimal(string? condition = null, string? molt = null) =>
        new(CrabLotId: LotId, WeightGram: 200m, Condition: condition, MoltingStage: molt);

    [Fact]
    public async Task AddCrab_PersistsEveryOwnerFieldTheMobileFormCollects()
    {
        var boxId = Guid.NewGuid();
        var req = new MobileAddCrabRequest(
            CrabLotId: LotId,
            Tag: "CRAB-1757000000",
            WeightGram: 220m,
            MoltingStage: "hard",
            Gender: "female",
            CrabType: "Cua biển",
            Condition: "premolt",
            Notes: "Mai hơi mòn",
            CarapaceLengthMm: 95m,
            CarapaceWidthMm: 72m,
            ImageUrls: ["https://cdn/1.jpg", "https://cdn/2.jpg"]);

        var result = await Create(boxId).AddCrabAsync(boxId, req);

        result.Success.Should().BeTrue();
        _saved.Should().NotBeNull();
        var crab = _saved!;

        crab.BoxId.Should().Be(boxId);
        crab.CrabLotId.Should().Be(LotId);
        crab.Tag.Should().Be("CRAB-1757000000");
        crab.WeightGram.Should().Be(220m);
        crab.InitialWeightGram.Should().Be(220m);
        crab.Gender.Should().Be(CrabGender.Female);
        crab.CrabType.Should().Be("Cua biển");
        crab.Condition.Should().Be(CrabCondition.Premolt);
        crab.Notes.Should().Be("Mai hơi mòn");
        crab.CarapaceLengthMm.Should().Be(95m);
        crab.CarapaceWidthMm.Should().Be(72m);
        crab.ImageUrlsJson.Should().Contain("cdn/1.jpg").And.Contain("cdn/2.jpg");
        crab.MoltingStage.Should().Be("hard");
    }

    [Fact]
    public async Task AddCrab_AllocatesSystemCodeAndCrabQrRow()
    {
        var boxId = Guid.NewGuid();

        await Create(boxId).AddCrabAsync(boxId, Minimal());

        _saved!.Code.Should().Be("CRAB-0001");
        _saved.QrCode.Should().Be("QR-CRAB-0001");
        _savedQr.Should().NotBeNull();
        _savedQr!.Code.Should().Be("QR-CRAB-0001");
        _savedQr.EntityType.Should().Be("crab");
        _savedQr.CrabId.Should().Be(_saved.Id);
        _savedQr.BoxId.Should().Be(boxId);
        _savedQr.Payload.Should().Contain("CRABSENSE:CRAB:CRAB-0001");
    }

    [Fact]
    public async Task AddCrab_SecondCrabInAnotherBox_GetsItsOwnCodeInsteadOfDuplicateKey()
    {
        // Used to store Code="" for the first crab, so this second add hit IX_Crabs_Code -> 500.
        var firstBox = Guid.NewGuid();
        var secondBox = Guid.NewGuid();

        await Create(firstBox).AddCrabAsync(firstBox, Minimal());
        var firstCode = _saved!.Code;

        _saved = null;
        var act = async () => await Create(secondBox).AddCrabAsync(secondBox, Minimal());

        await act.Should().NotThrowAsync();
        _saved!.Code.Should().NotBeNullOrWhiteSpace();
        _saved.Code.Should().NotBe(firstCode);
        _table.Select(c => c.Code).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    // Explicit owner stance wins over the molting stage, and drives lifecycle status.
    [InlineData("problem", "hard", CrabCondition.Problem, CrabStatus.Quarantined)]
    [InlineData("molting", "hard", CrabCondition.Molting, CrabStatus.Molting)]
    [InlineData("normal", "hard", CrabCondition.Normal, CrabStatus.Alive)]
    // No condition sent -> derive from molting stage (same rule as desktop).
    [InlineData(null, "soft", CrabCondition.Softshell, CrabStatus.Alive)]
    [InlineData(null, "premolt", CrabCondition.Premolt, CrabStatus.Alive)]
    [InlineData(null, null, CrabCondition.Normal, CrabStatus.Alive)]
    public async Task AddCrab_ResolvesConditionAndStatus(
        string? condition, string? moltingStage,
        CrabCondition expectedCondition, CrabStatus expectedStatus)
    {
        var boxId = Guid.NewGuid();

        await Create(boxId).AddCrabAsync(boxId, Minimal(condition, moltingStage));

        _saved!.Condition.Should().Be(expectedCondition);
        _saved.Status.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("male", CrabGender.Male)]
    [InlineData("Đực", CrabGender.Male)]
    [InlineData("female", CrabGender.Female)]
    [InlineData("Cái", CrabGender.Female)]
    // Free text from a client must never throw - fall back to Unknown.
    [InlineData("khong-ro", CrabGender.Unknown)]
    [InlineData(null, CrabGender.Unknown)]
    public async Task AddCrab_MapsGenderTolerantly(string? gender, CrabGender expected)
    {
        var boxId = Guid.NewGuid();
        var req = new MobileAddCrabRequest(
            CrabLotId: LotId, WeightGram: 200m, Gender: gender);

        await Create(boxId).AddCrabAsync(boxId, req);

        _saved!.Gender.Should().Be(expected);
    }

    [Fact]
    public async Task AddCrab_BlankCrabTypeAndNotes_AreStoredAsNullNotEmptyString()
    {
        var boxId = Guid.NewGuid();
        var req = new MobileAddCrabRequest(
            CrabLotId: LotId, WeightGram: 200m, CrabType: "   ", Notes: "  ");

        await Create(boxId).AddCrabAsync(boxId, req);

        _saved!.CrabType.Should().BeNull();
        _saved.Notes.Should().BeNull();
    }

    [Fact]
    public async Task AddCrab_BlankTag_FallsBackToAllocatedCode()
    {
        var boxId = Guid.NewGuid();

        await Create(boxId).AddCrabAsync(boxId, Minimal());

        _saved!.Tag.Should().Be(_saved.Code);
    }
}
