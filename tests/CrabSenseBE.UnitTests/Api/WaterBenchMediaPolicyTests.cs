using System.Security.Cryptography;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using SkiaSharp;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchMediaPolicyTests
{
    private static BenchMediaUpload Image()
    {
        using var bitmap=new SKBitmap(32,32);bitmap.Erase(SKColors.Beige);
        using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Jpeg,90);
        var bytes=encoded.ToArray();var now=DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"0123456789abcdef","abcdef0123456789",
            "NO2","abcdef0123456789-1",1,2,32,32,WaterBenchMediaPolicy.Crc(bytes),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),now,now,"000000000001","000000000002",Convert.ToBase64String(bytes));
    }
    [Fact]
    public void DecodeAndVerifyBytes()
    {
        var e=Image();Assert.NotEmpty(WaterBenchMediaPolicy.Validate(e));
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(e with{Sha256=new string('0',64)}));
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(e with{Crc32=e.Crc32^1}));
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(e with{Width=33}));
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(e with{SessionId=e.BatchId+"-2"}));
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(e with{RequestedAt=e.ReceivedAt.AddMinutes(-2)}));
    }
    private static BenchMediaUpload ReplaceBytes(BenchMediaUpload e,byte[] bytes)=>e with
    {
        JpegBase64=Convert.ToBase64String(bytes),
        Crc32=WaterBenchMediaPolicy.Crc(bytes),
        Sha256=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
    };

    [Fact]
    public void RejectCorruptJpegEvenWithMatchingChecksums()
    {
        var e=Image();
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(ReplaceBytes(e,new byte[]{0xff,0xd8,0xff,0xd9})));
    }
    [Fact]
    public void RejectRotatedExifJpegEvenWithMatchingChecksums()
    {
        var e=Image();
        var original=Convert.FromBase64String(e.JpegBase64);
        // APP1 Exif, little-endian TIFF, IFD0 orientation tag 0x0112 = 6 (rotate 90 degrees).
        byte[] exif={0xff,0xe1,0x00,0x22,0x45,0x78,0x69,0x66,0,0,
            0x49,0x49,0x2a,0,8,0,0,0,1,0,
            0x12,0x01,3,0,1,0,0,0,6,0,0,0,0,0,0,0};
        var rotated=original.Take(2).Concat(exif).Concat(original.Skip(2)).ToArray();
        using var data=SKData.CreateCopy(rotated);
        using var codec=SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedOrigin.RightTop,codec!.EncodedOrigin);
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(ReplaceBytes(e,rotated)));
    }
    [Fact]
    public void RejectPngEvenWithMatchingChecksums()
    {
        var e=Image();
        using var bitmap=new SKBitmap(32,32);
        using var image=SKImage.FromBitmap(bitmap);
        using var encoded=image.Encode(SKEncodedImageFormat.Png,100);
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Validate(ReplaceBytes(e,encoded.ToArray())));
    }
    [Fact]
    public void FolderUsesCanonicalPrivateWaterRoot()
    {
        var id=Guid.Parse("00000000-0000-0000-0000-000000000001");
        Assert.Equal("04_PhanTichNuoc/AREA-A06/2026/11/26/WaterBench/00000000000000000000000000000001/0123456789abcdef/abcdef0123456789/NO2/abcdef0123456789-1/Raw",
            MediaStorageLayout.WaterBench("AREA-A06",id,"0123456789abcdef","abcdef0123456789","NO2","abcdef0123456789-1",new DateTime(2026,11,26)));
    }
    [Fact]
    public void CorrelateActualCaptureRequest()
    {
        var e=Image();var t=new WaterBenchBatchTimeline();
        var start=new BenchBatchEvent(e.StationId,Guid.NewGuid(),e.ConnectionId,e.BootId,e.BatchId,1,"start",e.RequestedAt,Mode:"manual",Mask:1);
        t.Apply(start);
        var s=start with{EventId=Guid.NewGuid(),Sequence=2,Type="session",Analyte=e.Analyte,SessionId=e.SessionId};
        t.Apply(s);t.Apply(s with{EventId=Guid.NewGuid(),Sequence=3,Type="reaction",ReactionStartMs=1,ReactionDurationMs=300000});
        var req=s with{EventId=Guid.NewGuid(),Sequence=4,Type="image",CaptureId=e.CaptureId};t.Apply(req);
        WaterBenchMediaPolicy.Match(e,t,req);
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Match(e with{ConnectionId=Guid.NewGuid()},t,req));
        Assert.Throws<AppException>(()=>WaterBenchMediaPolicy.Match(e with{CaptureId=2},t,req));
    }
}
