using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchReceiptPolicyTests
{
    private static WaterBenchEvidence Row()=>new(){StationId=Guid.NewGuid(),EventId=Guid.NewGuid(),ConnectionId=Guid.NewGuid(),
        BootId="0123456789abcdef",FirmwareBatchId="abcdef0123456789",Kind="batch_start",KioskReceivedAt=DateTime.UtcNow};
    private static void Set(WaterBenchEvidence row,object value)
    {
        row.PayloadJson=JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        row.PayloadSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.PayloadJson))).ToLowerInvariant();
    }
    [Fact]
    public void TypedBatchReceiptBindsIdentityAndCanonicalDigest()
    {
        var r=Row();var e=new BenchBatchEvent(r.StationId,r.EventId,r.ConnectionId,r.BootId!,r.FirmwareBatchId!,1,"start",
            DateTimeOffset.UtcNow,Mode:"manual",Mask:1);
        Set(r,e);var v=WaterBenchReceiptPolicy.Read(r);
        Assert.Equal(r.Id,v.ReceiptId);Assert.Equal(r.EventId,v.EventId);Assert.Equal("batch",v.Kind);Assert.Equal(1,v.Sequence);
        Set(r,e with{EventId=Guid.NewGuid()});
        Assert.Throws<AppException>(()=>WaterBenchReceiptPolicy.Read(r));
        Set(r,e);r.PayloadSha256=new string('0',64);
        Assert.Throws<AppException>(()=>WaterBenchReceiptPolicy.Read(r));
    }
    [Fact]
    public void MediaReceiptExposesOnlyMediaIdentityAndDigest()
    {
        var r=Row();r.Kind="jpeg";r.Analyte="NO2";r.CaptureId=1;r.FirmwareSessionId=r.FirmwareBatchId+"-1";
        var media=Guid.NewGuid();var sha=new string('a',64);
        Set(r,new{mediaId=media,sha256=sha,contract="water-bench-media-v1"});
        var v=WaterBenchReceiptPolicy.Read(r);
        Assert.Equal("media",v.Kind);Assert.Equal(media,v.MediaId);Assert.Equal(sha,v.Sha256);Assert.Null(v.Sequence);
        Set(r,new{mediaId=Guid.Empty,sha256=sha,contract="water-bench-media-v1"});
        Assert.Throws<AppException>(()=>WaterBenchReceiptPolicy.Read(r));
    }
    [Fact]
    public void Nh3ReceiptDoesNotIncludeSensorPayload()
    {
        var r=Row();r.Kind="sample_evidence";r.Analyte="NH3";r.FirmwareSessionId="1111222233334444";
        Set(r,new Nh3EvidenceEvent(r.StationId,r.EventId,r.ConnectionId,r.BootId!,r.FirmwareBatchId!,r.FirmwareSessionId,
            "sample",DateTimeOffset.UtcNow,"000000000001","000000000002",new Nh3SampleReading(1,2,7.5m,3,28m,3000)));
        var v=WaterBenchReceiptPolicy.Read(r);Assert.Equal("nh3",v.Kind);Assert.Null(v.MediaId);
        Assert.Equal(r.FirmwareSessionId,v.SessionId);
    }
}
