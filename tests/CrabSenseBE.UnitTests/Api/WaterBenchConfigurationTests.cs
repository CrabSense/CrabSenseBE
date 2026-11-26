using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchConfigurationTests
{
    [Fact]
    public void DailyCalendarMatchesKiosk()
    {
        Assert.Equal(new[]{"00:10","23:59"},WaterBenchConfiguration.Times(new[]{"23:59","00:10"},true,5));
        Assert.Throws<AppException>(()=>WaterBenchConfiguration.Times(new[]{"23:59","00:01"},true,5));
        Assert.Throws<AppException>(()=>WaterBenchConfiguration.Times(new[]{"07:00","07:00"},true,5));
        Assert.Throws<AppException>(()=>WaterBenchConfiguration.Times(new[]{"7:00"},true,5));
        Assert.Throws<AppException>(()=>WaterBenchConfiguration.Times(Array.Empty<string>(),true,5));
        Assert.Empty(WaterBenchConfiguration.Times(Array.Empty<string>(),false,5));
        Assert.True(WaterBenchConfiguration.HardwareId("012345abcdef"));
        Assert.False(WaterBenchConfiguration.HardwareId("012345ABCDEF"));
    }
    [Fact]
    public void MigrationHasCrossRoleClaimAndAreaGuard()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql=db.GetService<IMigrator>().GenerateScript("20261126090000_WaterBenchEvidence","20261126100000_WaterBenchConfiguration");
        Assert.Contains("CREATE TABLE be.\"WaterBenchHardwareClaims\"",sql);
        Assert.Contains("CREATE TRIGGER water_bench_claim_hardware",sql);
        Assert.Contains("CREATE TRIGGER water_bench_keep_area",sql);
        Assert.DoesNotContain("DROP TABLE",sql);
    }
}
