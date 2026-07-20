using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>Unit test: upload media → Drive/local + share link.</summary>
public class MediaServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMediaStorageService> _storage = new();

    private MediaService Create() => new(_uow.Object, _storage.Object);

    [Fact]
    public async Task Upload_Image_SavesAssetWithShareLink()
    {
        _storage.SetupGet(s => s.ProviderName).Returns("GoogleDrive");
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), "image", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaUploadResult("file123", "https://drive.google.com/view", null, null, 1024));
        _storage.Setup(s => s.EnsureShareLinkAsync("file123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://drive.google.com/file/d/file123/view?usp=sharing");

        var repo = new Mock<IRepository<MediaAsset>>();
        repo.Setup(r => r.AddAsync(It.IsAny<MediaAsset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.MediaAssets).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await Create().UploadAsync(
            stream, "box.jpg", "image/jpeg",
            new MediaUploadMeta("image", Guid.NewGuid(), null, null, "box", null, null, true),
            Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data!.Category.Should().Be("image");
        result.Data.Provider.Should().Be("GoogleDrive");
        result.Data.ShareLink.Should().Contain("file123");
        result.Data.IsShared.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_InvalidCategory_Throws()
    {
        await using var stream = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAsync<CrabSenseBE.Application.Common.AppException>(() =>
            Create().UploadAsync(stream, "x.bin", "application/octet-stream",
                new MediaUploadMeta("pdf", null, null, null, null, null, null), null));
    }

    [Fact]
    public async Task Share_UpdatesLink()
    {
        var id = Guid.NewGuid();
        var asset = new MediaAsset { Id = id, StorageKey = "abc", IsShared = false };
        var repo = new Mock<IRepository<MediaAsset>>();
        repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        repo.Setup(r => r.Update(It.IsAny<MediaAsset>()));
        _uow.Setup(u => u.MediaAssets).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _storage.Setup(s => s.EnsureShareLinkAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://drive.google.com/shared");

        var result = await Create().ShareAsync(id);
        result.Data!.ShareLink.Should().Be("https://drive.google.com/shared");
        asset.IsShared.Should().BeTrue();
    }
}
