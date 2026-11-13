using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.SystemPlatform;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class CameraRecordingLibraryTests
{
    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CameraRecordingLibrary Library(AppDbContext db, IMediaStorageService storage) =>
        new(db, storage, NullLogger<CameraRecordingLibrary>.Instance);

    private static MediaAsset Video(Device camera, string name, DateTime at, long bytes = 4) => new()
    {
        Id = Guid.NewGuid(), DeviceId = camera.Id, FileName = name, CreatedAt = at,
        Category = "video", ContentType = "video/mp4", SizeBytes = bytes,
        Provider = "Local", StorageKey = name, RelatedEntityType = "device", RelatedEntityId = camera.Id
    };

    [Fact]
    public async Task List_filters_and_pages_without_leaking_farm_media()
    {
        await using var db = Context();
        var area = new FarmingArea { Id = Guid.NewGuid(), Code = "A", Name = "Area A" };
        var row = new FarmingRow { Id = Guid.NewGuid(), FarmingAreaId = area.Id, Code = "R", Name = "Row R" };
        var cam = new Device { Id = Guid.NewGuid(), DeviceCode = "CAM-01", DeviceType = "camera", FarmingAreaId = area.Id, FarmingRowId = row.Id };
        var controller = new Device { Id = Guid.NewGuid(), DeviceCode = "ESP-01", DeviceType = "esp32" };
        var at = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var first = Video(cam, "first.mp4", at, 10);
        var second = Video(cam, "second.mp4", at.AddHours(1), 20);
        var farm = Video(cam, "farm.mp4", at.AddHours(2));
        farm.CrabId = Guid.NewGuid();
        var detection = Video(cam, "detection.mp4", at.AddHours(2));
        var inconsistent = Video(cam, "wrong-device.mp4", at.AddHours(2));
        inconsistent.RelatedEntityId = controller.Id;
        db.FarmingAreas.Add(area); db.FarmingRows.Add(row); db.Devices.AddRange(cam,controller);
        db.MediaAssets.AddRange(first,second,farm,detection,inconsistent,Video(controller,"other.mp4",at));
        db.AiDetections.Add(new AiDetection { Id=Guid.NewGuid(), MediaId=detection.Id });
        await db.SaveChangesAsync();
        var library = Library(db, Mock.Of<IMediaStorageService>());
        var result = await library.ListAsync(new RecordingLibraryQuery(FarmingAreaId:area.Id,FarmingRowId:row.Id,
            From:new DateTimeOffset(at),To:new DateTimeOffset(at.AddDays(1)),Search:"cam-01",Page:1,PageSize:1));
        Assert.Equal(2,result.TotalCount); Assert.Equal(30,result.TotalBytes); Assert.Equal(1,result.CameraCount);
        Assert.Equal(2,result.TotalPages);
        Assert.Equal(second.Id,Assert.Single(result.Items).Id);
        Assert.Equal("Area A",Assert.Single(result.Items).AreaName);
        Assert.Equal("Row R",Assert.Single(result.Items).RowName);
        Assert.Null(Assert.Single(result.Items).StartedAt);
        Assert.Equal(404,(await Assert.ThrowsAsync<AppException>(()=>library.DetailAsync(farm.Id))).StatusCode);
        Assert.Equal(404,(await Assert.ThrowsAsync<AppException>(()=>library.DetailAsync(detection.Id))).StatusCode);
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>library.ListAsync(new RecordingLibraryQuery(PageSize:201)))).StatusCode);
        Assert.Equal(400,(await Assert.ThrowsAsync<AppException>(()=>library.ListAsync(new RecordingLibraryQuery(From:new DateTimeOffset(at),To:new DateTimeOffset(at))))).StatusCode);
    }

    [Fact]
    public async Task Playback_checks_provider_and_private_access_and_returns_seekable_content()
    {
        await using var db = Context();
        var camera = new Device { Id=Guid.NewGuid(), DeviceType="camera", DeviceCode="CAM" };
        var video = Video(camera,"file.mp4",DateTime.UtcNow);
        db.Devices.Add(camera);db.MediaAssets.Add(video);await db.SaveChangesAsync();
        var storage = new Mock<IMediaStorageService>(MockBehavior.Strict);
        storage.SetupGet(x=>x.ProviderName).Returns("Local");
        storage.Setup(x=>x.EnsurePrivateAccessAsync(video.StorageKey,It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(x=>x.DownloadAsync(video.StorageKey,It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream([1,2,3,4]));
        var file=await Library(db,storage.Object).OpenAsync(video.Id);
        await using var stream=file.Content;
        Assert.True(stream.CanSeek);Assert.Equal("video/mp4",file.ContentType);Assert.Equal(4,stream.Length);
        storage.Verify(x=>x.EnsurePrivateAccessAsync(video.StorageKey,It.IsAny<CancellationToken>()),Times.Once);
        Assert.Equal(404,(await Assert.ThrowsAsync<AppException>(()=>Library(db,storage.Object).OpenAsync(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Delete_keeps_metadata_on_storage_failure_and_audits_success()
    {
        await using var db=Context();
        var camera=new Device { Id=Guid.NewGuid(), DeviceType="camera", DeviceCode="CAM" };
        var video=Video(camera,"file.mp4",DateTime.UtcNow);
        db.Devices.Add(camera);db.MediaAssets.Add(video);await db.SaveChangesAsync();
        var storage=new Mock<IMediaStorageService>();
        storage.SetupGet(x=>x.ProviderName).Returns("Local");
        storage.Setup(x=>x.EnsurePrivateAccessAsync(video.StorageKey,It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(x=>x.DeleteAsync(video.StorageKey,It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("storage failed"));
        var actor=Guid.NewGuid();
        var library=Library(db,storage.Object);
        Assert.Equal(502,(await Assert.ThrowsAsync<AppException>(()=>library.DeleteAsync(video.Id,actor,null))).StatusCode);
        Assert.True(await db.MediaAssets.AnyAsync(x=>x.Id==video.Id));
        Assert.Contains(await db.OperationLogs.ToListAsync(),x=>x.Action=="DeleteCameraRecordingFailed");
        storage.Setup(x=>x.DeleteAsync(video.StorageKey,It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await library.DeleteAsync(video.Id,actor,null);
        Assert.False(await db.MediaAssets.AnyAsync(x=>x.Id==video.Id));
        Assert.Contains(await db.OperationLogs.ToListAsync(),x=>x.Action=="DeleteCameraRecording"&&x.UserId==actor);
    }
}
