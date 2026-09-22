using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

public class CrabImageServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPublicImageStorage> _storage = new();
    private readonly Mock<IRepository<Crab>> _crabs = new();
    private readonly Mock<IRepository<MediaAsset>> _media = new();

    public CrabImageServiceTests()
    {
        _uow.Setup(u => u.Crabs).Returns(_crabs.Object);
        _uow.Setup(u => u.MediaAssets).Returns(_media.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _media.Setup(r => r.AddAsync(It.IsAny<MediaAsset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _storage.SetupGet(s => s.ProviderName).Returns("S3");
    }

    private CrabImageService Create() => new(_uow.Object, _storage.Object);

    [Fact]
    public async Task Upload_WithoutCrab_ReturnsS3Urls()
    {
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), "a.jpg", "image/jpeg", "NhapHang/_pending", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaUploadResult("media/crabs/a.jpg",
                "https://bucket.s3.amazonaws.com/a.jpg",
                "https://bucket.s3.amazonaws.com/a.jpg",
                "https://bucket.s3.amazonaws.com/a.jpg", 12));

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await Create().UploadAsync(
            new[] { new CrabImageFile(stream, "a.jpg", "image/jpeg") },
            crabId: null, uploadedBy: Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull().And.HaveCount(1);
        result.Data![0].Url.Should().StartWith("https://");
        result.Data[0].Provider.Should().Be("S3");
        _crabs.Verify(r => r.Update(It.IsAny<Crab>()), Times.Never);
    }

    [Fact]
    public async Task Upload_WithCrabId_AppendsUrls()
    {
        var crabId = Guid.NewGuid();
        var crab = new Crab { Id = crabId, ImageUrlsJson = """["https://old.jpg"]""" };
        _crabs.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        var lots = new Mock<IRepository<CrabLot>>();
        _uow.Setup(u => u.CrabLots).Returns(lots.Object);
        var folder = $"NhapHang/_pending/{crabId:D}";
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), "b.png", "image/png", folder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaUploadResult("k", "https://new.png", "https://new.png", "https://new.png", 4));

        await using var stream = new MemoryStream(new byte[] { 1 });
        var result = await Create().UploadAsync(
            new[] { new CrabImageFile(stream, "b.png", "image/png") },
            crabId, Guid.NewGuid());

        result.Success.Should().BeTrue();
        JsonStringList.Parse(crab.ImageUrlsJson).Should().Equal("https://old.jpg", "https://new.png");
        _crabs.Verify(r => r.Update(crab), Times.Once);
    }

    [Fact]
    public async Task Upload_EmptyFiles_Throws()
    {
        await Assert.ThrowsAsync<AppException>(() =>
            Create().UploadAsync(Array.Empty<CrabImageFile>(), null, null));
    }

    [Fact]
    public async Task Upload_UnsupportedType_Throws()
    {
        await using var stream = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<AppException>(() =>
            Create().UploadAsync(
                new[] { new CrabImageFile(stream, "note.pdf", "application/pdf") },
                null, null));
    }

    [Fact]
    public async Task GetPhoto_DownloadsByStorageKey()
    {
        var crabId = Guid.NewGuid();
        var url = "https://bucket.s3.ap-southeast-1.amazonaws.com/media/crabs/a.jpg";
        var crab = new Crab { Id = crabId, ImageUrlsJson = $"""["{url}"]""" };
        _crabs.Setup(r => r.GetByIdAsync(crabId, It.IsAny<CancellationToken>())).ReturnsAsync(crab);
        _media.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<MediaAsset, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new MediaAsset
                {
                    CrabId = crabId,
                    ShareLink = url,
                    StorageKey = "media/crabs/a.jpg",
                    ContentType = "image/jpeg",
                    FileName = "a.jpg"
                }
            });
        _storage.Setup(s => s.DownloadAsync("media/crabs/a.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[] { 7, 7, 7 }));

        var photo = await Create().GetPhotoAsync(crabId, 0);

        photo.Should().NotBeNull();
        photo!.ContentType.Should().Be("image/jpeg");
        photo.FileName.Should().Be("a.jpg");
        photo.Data.Length.Should().Be(3);
    }
}
