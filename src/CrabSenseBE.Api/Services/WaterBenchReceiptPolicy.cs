using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Api.Services;

public sealed record WaterBenchReceiptView(Guid EventId,Guid ReceiptId,string Kind,string EvidenceKind,Guid ConnectionId,
    string? SessionId,int? CaptureId,string? Analyte,int? Sequence,DateTime? KioskReceivedAt,DateTime RecordedAt,
    string ServerPayloadSha256,Guid? MediaId=null,string? Sha256=null);

public static class WaterBenchReceiptPolicy
{
    public static bool IsDigest(string? s)=>s is not null&&Regex.IsMatch(s,@"\A[0-9a-f]{64}\z",RegexOptions.CultureInvariant);
    private static void Require(bool ok)
    {if(!ok)throw AppException.Conflict("Stored WaterBench receipt identity or digest mismatch.");}
    public static WaterBenchReceiptView Read(WaterBenchEvidence row)
    {
        Require(row.Id!=Guid.Empty&&row.EventId!=Guid.Empty&&row.StationId!=Guid.Empty&&row.ConnectionId!=Guid.Empty&&
            WaterBenchBatchTimeline.Hex16(row.BootId)&&WaterBenchBatchTimeline.Hex16(row.FirmwareBatchId)&&
            row.PayloadJson is not null&&row.PayloadJson.Length<=65536&&IsDigest(row.PayloadSha256)&&
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.PayloadJson!))).ToLowerInvariant()==row.PayloadSha256);
        string kind;int? sequence=null;Guid? mediaId=null;string? sha=null;
        try
        {
            if(row.Kind=="jpeg")
            {
                kind="media";
                using var doc=JsonDocument.Parse(row.PayloadJson!);
                var e=doc.RootElement;
                mediaId=e.GetProperty("mediaId").GetGuid();sha=e.GetProperty("sha256").GetString();
                Require(mediaId!=Guid.Empty&&IsDigest(sha)&&e.GetProperty("contract").GetString()=="water-bench-media-v1"&&
                    WaterBenchBatchTimeline.Bit(row.Analyte)!=0&&row.CaptureId is >=1 and <=65536&&
                    row.FirmwareSessionId!=null&&row.KioskReceivedAt.HasValue);
            }
            else if(row.Kind is "sample_evidence" or "nh3_image_link")
            {
                kind="nh3";
                var e=JsonSerializer.Deserialize<Nh3EvidenceEvent>(row.PayloadJson!,WaterBatchPlanning.Json)
                    ??throw AppException.Conflict("Invalid stored NH3 receipt.");
                Require(e.StationId==row.StationId&&e.EventId==row.EventId&&e.ConnectionId==row.ConnectionId&&
                    e.BootId==row.BootId&&e.BatchId==row.FirmwareBatchId&&e.SessionId==row.FirmwareSessionId&&
                    row.Analyte=="NH3"&&e.Image?.CaptureId==row.CaptureId&&
                    e.Type==(row.Kind=="sample_evidence"?"sample":"image_link"));
            }
            else
            {
                kind="batch";
                var e=JsonSerializer.Deserialize<BenchBatchEvent>(row.PayloadJson!,WaterBatchPlanning.Json)
                    ??throw AppException.Conflict("Invalid stored batch receipt.");
                Require(e.StationId==row.StationId&&e.EventId==row.EventId&&e.ConnectionId==row.ConnectionId&&
                    e.BootId==row.BootId&&e.BatchId==row.FirmwareBatchId&&e.SessionId==row.FirmwareSessionId&&
                    e.CaptureId==row.CaptureId&&e.Analyte==row.Analyte&&
                    e.Sequence is >=1 and <=WaterBenchBatchTimeline.MaxEvents&&WaterBenchBatchTimeline.EvidenceKind(e.Type)==row.Kind);
                sequence=e.Sequence;
            }
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {throw AppException.Conflict("Invalid stored WaterBench receipt.");}
        return new(row.EventId,row.Id,kind,row.Kind,row.ConnectionId,row.FirmwareSessionId,row.CaptureId,row.Analyte,
            sequence,row.KioskReceivedAt,row.CreatedAt,row.PayloadSha256,mediaId,sha);
    }
}
