using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CrabSenseBE.Application.Common;
using SkiaSharp;

namespace CrabSenseBE.Api.Services;

public sealed record BenchMediaUpload(Guid StationId,Guid EventId,Guid ConnectionId,string BootId,string BatchId,
    string Analyte,string SessionId,int CaptureId,uint Sequence,int Width,int Height,uint Crc32,string Sha256,
    DateTimeOffset ReceivedAt,DateTimeOffset RequestedAt,string MasterHardwareId,string SlaveHardwareId,string JpegBase64);

public static class WaterBenchMediaPolicy
{
    public const int MaxBytes=55*1024;
    public static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc=uint.MaxValue;
        foreach(byte b in bytes)
        {
            crc^=b;
            for(int j=0;j<8;j++)crc=(crc&1)!=0?(crc>>1)^0xedb88320u:crc>>1;
        }
        return ~crc;
    }
    public static byte[] Validate(BenchMediaUpload e)
    {
        if(e.StationId==Guid.Empty||e.EventId==Guid.Empty||e.ConnectionId==Guid.Empty||
            !WaterBenchBatchTimeline.Hex16(e.BootId)||!WaterBenchBatchTimeline.Hex16(e.BatchId)||
            WaterBenchBatchTimeline.Bit(e.Analyte)==0||
            !(e.Analyte=="NH3"?WaterBenchBatchTimeline.Hex16(e.SessionId):e.SessionId==e.BatchId+(e.Analyte=="NO2"?"-1":"-2"))||
            e.CaptureId is <1 or >65536||e.Width<=0||e.Height<=0||(long)e.Width*e.Height>1_000_000||
            !WaterBenchConfiguration.HardwareId(e.MasterHardwareId)||!WaterBenchConfiguration.HardwareId(e.SlaveHardwareId)||
            e.Sha256 is null||!Regex.IsMatch(e.Sha256,@"\A[0-9a-f]{64}\z",RegexOptions.CultureInvariant)||
            e.ReceivedAt==default||e.RequestedAt==default||e.ReceivedAt>DateTimeOffset.UtcNow.AddMinutes(5)||
            e.RequestedAt>DateTimeOffset.UtcNow.AddMinutes(5)||Math.Abs((e.ReceivedAt-e.RequestedAt).TotalSeconds)>60||
            string.IsNullOrEmpty(e.JpegBase64)||e.JpegBase64.Length>((MaxBytes+2)/3)*4)
            throw AppException.BadRequest("Invalid WaterBench JPEG envelope.");
        byte[] bytes;
        try{bytes=Convert.FromBase64String(e.JpegBase64);}
        catch(FormatException){throw AppException.BadRequest("Invalid JPEG base64.");}
        if(bytes.Length<4||bytes.Length>MaxBytes||bytes[0]!=0xff||bytes[1]!=0xd8||bytes[^2]!=0xff||bytes[^1]!=0xd9||
            Crc(bytes)!=e.Crc32||Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=e.Sha256)
            throw AppException.BadRequest("JPEG size, envelope, CRC or SHA-256 mismatch.");
        using var data=SKData.CreateCopy(bytes);
        using var codec=SKCodec.Create(data);
        if(codec is null||codec.EncodedFormat!=SKEncodedImageFormat.Jpeg)
            throw AppException.BadRequest("Invalid JPEG header.");
        if(codec.Info.Width!=e.Width||codec.Info.Height!=e.Height)
            throw AppException.BadRequest("JPEG dimensions mismatch.");
        // Skia reports zero frames for some non-animated codecs. JPEG is static;
        // zero or one is valid, but multiple frames are not supported.
        // Match the existing WaterColorAnalyzer contract, while requiring JPEG.
        if(codec.FrameCount>1)
            throw AppException.BadRequest("Multiple JPEG frames are not supported.");
        if(codec.EncodedOrigin!=SKEncodedOrigin.TopLeft)
            throw AppException.BadRequest("JPEG orientation mismatch.");
        var info=new SKImageInfo(e.Width,e.Height,SKColorType.Rgba8888,SKAlphaType.Opaque);
        var pixels=new byte[checked(e.Width*e.Height*4)];
        if(codec.GetPixels(info,pixels)!=SKCodecResult.Success)throw AppException.BadRequest("Incomplete JPEG decode.");
        return bytes;
    }
    public static void Match(BenchMediaUpload e,WaterBenchBatchTimeline timeline,BenchBatchEvent request)
    {
        var c=timeline.Channels.SingleOrDefault(x=>x.Analyte==e.Analyte);
        if(timeline.StationId!=e.StationId||timeline.BootId!=e.BootId||timeline.BatchId!=e.BatchId||
            timeline.ConnectionId!=e.ConnectionId||c is null||c.SessionId!=e.SessionId||c.CaptureId!=e.CaptureId||
            request.Type!="image"||request.StationId!=e.StationId||request.ConnectionId!=e.ConnectionId||
            request.BootId!=e.BootId||request.BatchId!=e.BatchId||request.Analyte!=e.Analyte||
            request.SessionId!=e.SessionId||request.CaptureId!=e.CaptureId||
            Math.Abs((request.ReceivedAt-e.RequestedAt).TotalSeconds)>2)
            throw AppException.Conflict("Image request correlation mismatch.");
    }
}
