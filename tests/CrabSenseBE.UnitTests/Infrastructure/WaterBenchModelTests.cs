using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class WaterBenchModelTests
{
    [Fact]
    public void EvidenceIsScopedAndCannotCascadeDelete()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var model=db.GetService<IDesignTimeModel>().Model;
        var station=model.FindEntityType(typeof(WaterBenchStation))!;
        var evidence=model.FindEntityType(typeof(WaterBenchEvidence))!;
        Assert.Equal("be",station.GetSchema());
        Assert.Equal("WaterBenchEvidence",evidence.GetTableName());
        Assert.Contains(station.GetForeignKeys(),f=>f.PrincipalEntityType.ClrType==typeof(FarmKiosk)&&f.DeleteBehavior==DeleteBehavior.Restrict);
        Assert.Contains(evidence.GetForeignKeys(),f=>f.PrincipalEntityType.ClrType==typeof(WaterBenchStation)&&f.DeleteBehavior==DeleteBehavior.Restrict);
        Assert.Contains(evidence.GetIndexes(),i=>i.IsUnique&&i.Properties.Select(p=>p.Name).SequenceEqual(new[]{"StationId","EventId"}));
        Assert.Contains(station.GetIndexes(),i=>i.IsUnique&&i.Properties.Single().Name=="KioskId"&&i.GetFilter()=="\"RevokedAt\" IS NULL");
        Assert.Contains(evidence.GetCheckConstraints(),x=>x.Name=="CK_WaterBenchEvidence_Payload");
        Assert.Equal(64,evidence.FindProperty(nameof(WaterBenchEvidence.PayloadSha256))!.GetMaxLength());
        Assert.Contains("20261126090000_WaterBenchEvidence",db.Database.GetMigrations());
    }

    [Fact]
    public void MigrationIsAdditiveAndReversible()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var migrator=db.GetService<IMigrator>();
        string sql=migrator.GenerateScript("20261126003000_AddHarvestAssignmentWorkflowAudit","20261126090000_WaterBenchEvidence");
        Assert.Contains("CREATE TABLE be.\"WaterBenchStations\"",sql);
        Assert.Contains("CREATE TABLE be.\"WaterBenchEvidence\"",sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_WaterBenchEvidence_StationId_EventId\"",sql);
        Assert.DoesNotContain("DROP TABLE",sql);
        string down=migrator.GenerateScript("20261126090000_WaterBenchEvidence","20261126003000_AddHarvestAssignmentWorkflowAudit");
        Assert.Contains("DROP TABLE be.\"WaterBenchEvidence\"",down);
        Assert.DoesNotContain("DROP TABLE be.\"WaterAnalysisRuns\"",down);
    }
}
