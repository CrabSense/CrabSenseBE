using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.SystemPlatform;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class CameraManagementTests
{
    [Theory]
    [InlineData("rtsp://user:password@192.168.1.10/live")]
    [InlineData("http://192.168.1.10/capture?token=secret")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    public void Unsafe_camera_urls_are_not_exposed(string url)
    {
        Assert.Null(CameraConfiguration.SafeUrl(url));
        Assert.Equal(400, Assert.Throws<AppException>(() => CameraConfiguration.ValidateUrl(url)).StatusCode);
    }

    [Fact]
    public void Configured_urls_do_not_prove_camera_online()
    {
        var now = DateTime.UtcNow;
        var camera = new Device { DeviceType = "camera", Status = DeviceStatus.Online, StreamUrl = "rtsp://192.168.1.10/live" };
        Assert.Equal(camera.StreamUrl, CameraConfiguration.SafeUrl(camera.StreamUrl));
        Assert.Equal("Offline", CameraConfiguration.Status(camera, now));
        camera.LastSeenAt = now.AddMinutes(-16);
        Assert.Equal("Offline", CameraConfiguration.Status(camera, now));
        camera.LastSeenAt = now.AddMinutes(-1);
        Assert.Equal("Online", CameraConfiguration.Status(camera, now));
        camera.Status = DeviceStatus.Maintenance;
        Assert.Equal("Maintenance", CameraConfiguration.Status(camera, now));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"timezone\":\"Asia/Ho_Chi_Minh\",\"slots\":[{\"day\":8,\"start\":\"08:00\",\"end\":\"09:00\"}]}")]
    [InlineData("{\"timezone\":\"Asia/Ho_Chi_Minh\",\"slots\":[{\"day\":1,\"start\":\"10:00\",\"end\":\"09:00\"}]}")]
    [InlineData("{\"timezone\":\"Asia/Ho_Chi_Minh\",\"slots\":[{\"day\":1,\"start\":\"08:00\",\"end\":\"10:00\"},{\"day\":1,\"start\":\"09:00\",\"end\":\"11:00\"}]}")]
    public void Invalid_schedules_are_rejected(string json)
        => Assert.Equal(400, Assert.Throws<AppException>(() => CameraConfiguration.ValidateSchedule(json, true)).StatusCode);

    [Fact]
    public async Task Recording_configuration_uses_real_area_and_does_not_claim_runtime()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var area = new FarmingArea { Id = Guid.NewGuid(), Name = "Area A", Code = "A" };
        var camera = new Device { Id = Guid.NewGuid(), DeviceCode = "CAM-01", DeviceType = "camera",
            FarmingAreaId = area.Id, InstallationLocation = "Wall", StreamUrl = "rtsp://user:secret@192.168.1.10/live" };
        db.FarmingAreas.Add(area);
        db.Devices.Add(camera);
        await db.SaveChangesAsync();
        var service = new SystemPlatformService(db, Mock.Of<IDatabaseBackupService>(), new ConfigurationBuilder().Build());
        const string schedule = "{\"timezone\":\"Asia/Ho_Chi_Minh\",\"slots\":[{\"day\":1,\"start\":\"08:00\",\"end\":\"10:00\"}]}";
        var saved = (await service.UpsertRecordingAsync(camera.Id, new UpsertCameraRecordingRequest("Scheduled", 30, null, 15, 10, schedule), null, null)).Data!;
        Assert.Equal("Area A", saved.AreaName);
        Assert.Equal("NotIntegrated", saved.RuntimeStatus);
        Assert.Null(saved.StreamUrl);
        Assert.Equal("Area A", Assert.Single((await service.GetRecordingSettingsAsync()).Data!).AreaName);
        var result = (await service.TestDeviceConnectionAsync(camera.Id, null, null)).Data!;
        Assert.False(result.Reachable);
        Assert.Equal("NotIntegrated", result.VerificationStatus);
    }

    [Fact]
    public async Task Camera_logs_include_own_disconnect_but_not_farm_sensor_alerts()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var camera = new Device { Id = Guid.NewGuid(), DeviceCode = "CAM-01", DeviceType = "camera" };
        var sensor = new Sensor { Id = Guid.NewGuid(), DeviceId = camera.Id, SensorCode = "S1" };
        var own = new Alert { Id = Guid.NewGuid(), IncidentType = "device_disconnect", SourceType = "Device", SourceId = camera.Id.ToString("N"), Message = "offline" };
        var other = new Alert { Id = Guid.NewGuid(), IncidentType = "threshold_violation", SourceType = "Sensor", SensorId = sensor.Id, Message = "farm value" };
        db.Devices.Add(camera); db.Sensors.Add(sensor); db.Alerts.AddRange(own, other);
        await db.SaveChangesAsync();
        var service = new SystemPlatformService(db, Mock.Of<IDatabaseBackupService>(), new ConfigurationBuilder().Build());
        var logs = (await service.GetDeviceLogsAsync(camera.Id)).Data!;
        Assert.Equal(own.Id, Assert.Single(logs).Id);
    }
}
