using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchResultPolicyTests
{
    [Theory]
    [InlineData("NO2","mg/L NO2",true)]
    [InlineData("NO3","mg/L NO2",false)]
    [InlineData("NH3","mg/L NH4",true)]
    [InlineData("NH3","mg/L TAN-N",true)]
    [InlineData("NH3","mg/L",false)]
    public void UnitsAreNeverSilentlyConverted(string analyte,string unit,bool valid)=>
        Assert.Equal(valid,WaterBenchResultPolicy.ValidUnit(analyte,unit));
    [Fact]
    public void RequireServerMediaReceipt()
    {
        var id=Guid.NewGuid();var hash=new string('a',64);
        var r=WaterBenchResultPolicy.Receipt(JsonSerializer.Serialize(new{mediaId=id,sha256=hash,contract="water-bench-media-v1"}));
        Assert.Equal(id,r.MediaId);
        Assert.Throws<AppException>(()=>WaterBenchResultPolicy.Receipt(JsonSerializer.Serialize(new{mediaId=id,sha256=hash})));
    }
    [Fact]
    public void ReceiptMustBindVersionCalibrationAndCamera()
    {
        var cal=Guid.NewGuid();var camera=Guid.NewGuid();
        var r=new WaterBenchResultPolicy.CaptureReceipt(Guid.NewGuid(),new string('a',64),"water-bench-media-v1",cal,camera,1);
        Assert.True(WaterBenchResultPolicy.ReceiptMatches(r,cal,camera));
        Assert.False(WaterBenchResultPolicy.ReceiptMatches(r with{ConfigurationVersion=0},cal,camera));
        Assert.False(WaterBenchResultPolicy.ReceiptMatches(r,Guid.NewGuid(),camera));
        Assert.False(WaterBenchResultPolicy.ReceiptMatches(r,cal,Guid.NewGuid()));
    }
    [Fact]
    public void RequireTerminalAndSessionEvidence()
    {
        var t=new WaterBenchBatchTimeline();
        var e=new BenchBatchEvent(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"0123456789abcdef","abcdef0123456789",1,"start",
            DateTimeOffset.UtcNow,Mode:"manual",Mask:1);
        t.Apply(e);var c=Assert.Single(t.Channels);Assert.False(WaterBenchResultPolicy.ReadyForEstimate(t,c));
        var s=e with{EventId=Guid.NewGuid(),Sequence=2,Type="session",Analyte="NO2",SessionId=e.BatchId+"-1"};
        t.Apply(s);t.Apply(s with{EventId=Guid.NewGuid(),Sequence=3,Type="reaction",ReactionStartMs=0,ReactionDurationMs=300000});
        t.Apply(s with{EventId=Guid.NewGuid(),Sequence=4,Type="image",CaptureId=1});
        Assert.False(WaterBenchResultPolicy.ReadyForEstimate(t,c));
        t.Apply(e with{EventId=Guid.NewGuid(),Sequence=5,Type="status",State=2,Started=1,Reacting=0,Imaged=1,Drained=1});
        Assert.True(WaterBenchResultPolicy.ReadyForEstimate(t,c));
        Assert.False(t.ChemicalResultValidated);
    }
    [Fact]
    public void MigrationAddsOnlyAssessmentTable()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var sql=db.GetService<IMigrator>().GenerateScript("20261126100000_WaterBenchConfiguration","20261126110000_WaterBenchResultAssessments");
        Assert.Contains("CREATE TABLE be.\"WaterBenchResultAssessments\"",sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_WaterBenchResultAssessments_SessionEvidenceId\"",sql);
        Assert.DoesNotContain("DROP TABLE",sql);
    }
}
