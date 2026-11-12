using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
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
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(new MediaUploadResult("file123", "https://drive.google.com/view", null, null, 1024));
        _storage.Setup(s => s.EnsureShareLinkAsync("file123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://drive.google.com/file/d/file123/view?usp=sharing");

        var repo = new Mock<IRepository<MediaAsset>>();
        repo.Setup(r => r.AddAsync(It.IsAny<MediaAsset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.MediaAssets).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var boxId = Guid.NewGuid();
        var boxes = new Mock<IRepository<Box>>();
        boxes.Setup(r => r.GetByIdAsync(boxId, It.IsAny<CancellationToken>())).ReturnsAsync((Box?)null);
        _uow.Setup(u => u.Boxes).Returns(boxes.Object);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await Create().UploadAsync(
            stream, "box.jpg", "image/jpeg",
            new MediaUploadMeta("image", boxId, null, null, "box", null, null, true),
            Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Data!.Category.Should().Be("image");
        result.Data.Provider.Should().Be("GoogleDrive");
        result.Data.ShareLink.Should().Contain("file123");
        result.Data.IsShared.Should().BeTrue();
    }

    [Fact]
    public async Task Upload_PrivateMedia_DoesNotExposeProviderLinks()
    {
        MediaAsset? saved = null;
        _storage.SetupGet(s => s.ProviderName).Returns("GoogleDrive");
        _storage.Setup(s => s.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(new MediaUploadResult(
                "private-file", "https://provider/view", "https://provider/content",
                "https://provider/public", 3));
        var repo = new Mock<IRepository<MediaAsset>>();
        repo.Setup(r => r.AddAsync(It.IsAny<MediaAsset>(), It.IsAny<CancellationToken>()))
            .Callback<MediaAsset, CancellationToken>((asset, _) => saved = asset)
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.MediaAssets).Returns(repo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await Create().UploadAsync(
            stream, "internal.jpg", "image/jpeg",
            new MediaUploadMeta("image", null, null, null, null, null, null, false),
            Guid.NewGuid());

        result.Data!.IsShared.Should().BeFalse();
        result.Data.ShareLink.Should().BeNull();
        result.Data.StorageKey.Should().BeEmpty();
        saved!.ShareLink.Should().BeNull();
        _storage.Verify(s => s.EnsureShareLinkAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task Upload_BoxAndUnrelatedCrab_IsRejectedEvenWhenUserHasBothAreas()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var boxA = new Box { Id = Guid.NewGuid(), FarmingRowId = Guid.NewGuid() };
        var boxB = new Box { Id = Guid.NewGuid(), FarmingRowId = Guid.NewGuid() };
        var crabB = new Crab
        {
            Id = Guid.NewGuid(),
            BoxId = boxB.Id,
            CrabLotId = Guid.NewGuid()
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(Repo(new AppUser
        {
            Id = userId,
            Role = UserRole.FarmManager,
            IsActive = true
        }));
        uow.Setup(x => x.FarmManagerAssignments).Returns(Repo(
            new FarmManagerAssignment { ManagerUserId = userId, FarmingAreaId = areaA },
            new FarmManagerAssignment { ManagerUserId = userId, FarmingAreaId = areaB }));
        uow.Setup(x => x.Boxes).Returns(Repo(boxA, boxB));
        uow.Setup(x => x.FarmingRows).Returns(Repo(
            new FarmingRow { Id = boxA.FarmingRowId, FarmingAreaId = areaA },
            new FarmingRow { Id = boxB.FarmingRowId, FarmingAreaId = areaB }));
        uow.Setup(x => x.Crabs).Returns(Repo(crabB));
        uow.Setup(x => x.CrabBoxAllocations).Returns(Repo<CrabBoxAllocation>());
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        var service = new MediaService(
            uow.Object, _storage.Object, current.Object);

        await using var stream = new MemoryStream([1]);
        var act = () => service.UploadAsync(
            stream,
            "invalid.jpg",
            "image/jpeg",
            new MediaUploadMeta(
                "image", boxA.Id, crabB.Id, null, null, null, null, false),
            userId);

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(400);
        _storage.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task PrivateContent_StreamsOnlyInsideAssignedArea()
    {
        var userId = Guid.NewGuid();
        var areaA = Guid.NewGuid();
        var areaB = Guid.NewGuid();
        var rowA = new FarmingRow { Id = Guid.NewGuid(), FarmingAreaId = areaA };
        var rowB = new FarmingRow { Id = Guid.NewGuid(), FarmingAreaId = areaB };
        var boxA = new Box { Id = Guid.NewGuid(), FarmingRowId = rowA.Id };
        var boxB = new Box { Id = Guid.NewGuid(), FarmingRowId = rowB.Id };
        var allowed = new MediaAsset
        {
            Id = Guid.NewGuid(),
            BoxId = boxA.Id,
            StorageKey = "private/a.mp4",
            FileName = "a.mp4",
            ContentType = "video/mp4",
            SizeBytes = 4
        };
        var denied = new MediaAsset
        {
            Id = Guid.NewGuid(),
            BoxId = boxB.Id,
            StorageKey = "private/b.mp4",
            FileName = "b.mp4",
            ContentType = "video/mp4",
            SizeBytes = 4
        };
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(Repo(new AppUser
        {
            Id = userId,
            Role = UserRole.FarmManager,
            IsActive = true
        }));
        uow.Setup(x => x.FarmManagerAssignments).Returns(Repo(
            new FarmManagerAssignment
            {
                ManagerUserId = userId,
                FarmingAreaId = areaA
            }));
        uow.Setup(x => x.FarmingRows).Returns(Repo(rowA, rowB));
        uow.Setup(x => x.Boxes).Returns(Repo(boxA, boxB));
        uow.Setup(x => x.MediaAssets).Returns(Repo(allowed, denied));
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        var storage = new Mock<IMediaStorageService>();
        storage.Setup(x => x.EnsurePrivateAccessAsync(
                allowed.StorageKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storage.Setup(x => x.DownloadAsync(
                allowed.StorageKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3, 4]));
        var service = new MediaService(
            uow.Object, storage.Object, current.Object);

        var content = await service.OpenContentAsync(allowed.Id);
        content.ContentType.Should().Be("video/mp4");
        content.Content.CanSeek.Should().BeTrue();
        storage.Verify(x => x.EnsurePrivateAccessAsync(
            allowed.StorageKey, It.IsAny<CancellationToken>()), Times.Once);

        var act = () => service.OpenContentAsync(denied.Id);
        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(403);
        storage.Verify(x => x.DownloadAsync(
            denied.StorageKey, It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(x => x.EnsurePrivateAccessAsync(
            denied.StorageKey, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FarmStaff_CannotCreatePublicShare()
    {
        var userId = Guid.NewGuid();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.Users).Returns(Repo(new AppUser
        {
            Id = userId,
            Role = UserRole.FarmStaff,
            IsActive = true
        }));
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        current.SetupGet(x => x.UserId).Returns(userId);
        var service = new MediaService(
            uow.Object, _storage.Object, current.Object);

        var act = () => service.ShareAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<AppException>())
            .Which.StatusCode.Should().Be(403);
        _storage.Verify(x => x.EnsureShareLinkAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

    private static IRepository<T> Repo<T>(params T[] values) where T : class
    {
        var rows = values.ToList();
        var repo = new Mock<IRepository<T>>();
        repo.Setup(x => x.GetByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                rows.FirstOrDefault(row =>
                    (Guid)row.GetType().GetProperty("Id")!.GetValue(row)! == id));
        repo.Setup(x => x.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<T, bool>> predicate,
                CancellationToken _) => rows.Where(predicate.Compile()).ToArray());
        repo.Setup(x => x.FirstOrDefaultAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<T, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                System.Linq.Expressions.Expression<Func<T, bool>> predicate,
                CancellationToken _) => rows.FirstOrDefault(predicate.Compile()));
        return repo.Object;
    }
}
