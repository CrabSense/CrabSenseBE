using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class SystemOverviewScopeTests
{
    [Fact]
    public async Task Technical_alerts_and_logs_exclude_farm_incidents_and_operation_details()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var actor = Guid.NewGuid();
        var technical = new Alert { Id = Guid.NewGuid(), IncidentType = "device_disconnect",
            SourceType = "Device", Message = "Controller offline", Status = AlertStatus.Active };
        var operational = new Alert { Id = Guid.NewGuid(), IncidentType = "threshold_violation",
            SourceType = "Sensor", Message = "private farm value", Status = AlertStatus.Active };
        var recovered = new Alert { Id = Guid.NewGuid(), IncidentType = "sensor_disconnect",
            SourceType = "Sensor", Message = "Sensor recovered", Status = AlertStatus.Recovered };
        db.Alerts.AddRange(technical, operational, recovered);
        db.OperationLogs.AddRange(
            new OperationLog { Id = Guid.NewGuid(), UserId = actor, Action = "UpdateRecording", EntityType = "Device", Details = "private raw details" },
            new OperationLog { Id = Guid.NewGuid(), UserId = actor, Action = "HarvestCrab", EntityType = "Crab", Details = "private farm operation" });
        await db.SaveChangesAsync();
        var service = new SystemPlatformService(db, Mock.Of<IDatabaseBackupService>(), new ConfigurationBuilder().Build());

        var active = (await service.GetTechnicalAlertsAsync(true)).Data!;
        Assert.Equal(technical.Id, Assert.Single(active).Id);
        var all = (await service.GetTechnicalAlertsAsync(false)).Data!;
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, a => a.Id == operational.Id);

        var logs = (await service.GetLogsAsync(null, null, null, null, null)).Data!;
        Assert.Contains(logs, x => x.Message == "UpdateRecording");
        Assert.Contains(logs, x => x.Id == technical.Id);
        Assert.DoesNotContain(logs, x => x.Id == operational.Id || x.Message.Contains("private") || x.Message == "HarvestCrab");
        var audit = (await service.GetAuditLogsAsync()).Data!;
        Assert.Equal("UpdateRecording", Assert.Single(audit).Message);
    }
}
