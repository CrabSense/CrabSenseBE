using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Api.Services;

// Immutable edge journal observations, never proof of physical completion.
public sealed record WaterBenchAuditEvent(Guid StationId,Guid EventId,Guid ConnectionId,string BootId,
    string Kind,Guid JournalId,int Sequence,DateTimeOffset At,string Payload);

public static class WaterBenchAuditPolicy
{
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static Guid EventId(Guid station,string kind,Guid journal,int sequence)=>new(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"water-bench-audit-v1:{station:N}:{kind}:{journal:N}:{sequence}"))).AsSpan(0,16));
    private static void Need(bool ok)
    {if(!ok)throw AppException.BadRequest("Invalid water bench audit observation.");}
    private static JsonElement Parse(string payload)
    {
        using var doc=JsonDocument.Parse(payload,new JsonDocumentOptions{MaxDepth=16});
        Check(doc.RootElement);return doc.RootElement.Clone();
    }
    private static void Check(JsonElement e)
    {
        if(e.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in e.EnumerateObject()){Need(names.Add(p.Name));Check(p.Value);}
        }
        else if(e.ValueKind==JsonValueKind.Array)foreach(var item in e.EnumerateArray())Check(item);
    }
    public static void Validate(WaterBenchAuditEvent e)
    {
        Need(e.StationId!=Guid.Empty&&e.ConnectionId!=Guid.Empty&&e.JournalId!=Guid.Empty&&
            WaterBenchBatchTimeline.Hex16(e.BootId)&&e.Kind is "command" or "command_outcome" or "clean_status"&&
            e.EventId==EventId(e.StationId,e.Kind,e.JournalId,e.Sequence)&&e.At!=default&&e.At<=DateTimeOffset.UtcNow.AddMinutes(5)&&
            e.Payload is not null&&Encoding.UTF8.GetByteCount(e.Payload)<=16384);
        try
        {
            var p=Parse(e.Payload!);
            Need(p.GetProperty("version").GetInt32()==1&&p.GetProperty("connectionId").GetGuid()==e.ConnectionId&&
                p.GetProperty("bootId").GetString()==e.BootId&&p.GetProperty("at").GetDateTimeOffset()==e.At);
            if(e.Kind=="clean_status")
            {
                var type=p.GetProperty("type").GetString();
                Need(e.Sequence is >=1 and <=256&&p.GetProperty("sequence").GetInt32()==e.Sequence&&
                    p.GetProperty("stationId").GetGuid()==e.StationId&&p.GetProperty("runId").GetGuid()==e.JournalId&&
                    p.GetProperty("mask").GetInt32() is >=1 and <=7&&
                    (e.Sequence==1?type=="start":type is "command" or "ack" or "status" or "interrupted"));
            }
            else
            {
                Need(e.Sequence==0&&p.GetProperty("requestId").GetGuid()==e.JournalId&&p.GetProperty("sequence").GetUInt32()>0);
                var state=p.GetProperty("state").GetString();
                if(e.Kind=="command")
                    Need(state=="send_attempt"&&p.GetProperty("stationId").GetGuid()==e.StationId&&
                        p.GetProperty("request").GetProperty("requestId").GetGuid()==e.JournalId);
                else Need(state is "accepted" or "rejected" or "unknown"&&
                    WaterBenchReceiptPolicy.IsDigest(p.GetProperty("attemptSha256").GetString()));
            }
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        {throw AppException.BadRequest("Malformed water bench audit payload.");}
    }
    public static void ValidateOutcome(WaterBenchAuditEvent e,WaterBenchAuditEvent attempt)
    {
        var p=Parse(e.Payload);var a=Parse(attempt.Payload);
        if(e.ConnectionId!=attempt.ConnectionId||e.BootId!=attempt.BootId||
            p.GetProperty("sequence").GetUInt32()!=a.GetProperty("sequence").GetUInt32()||
            p.GetProperty("attemptSha256").GetString()!=Hash(attempt.Payload))
            throw AppException.Conflict("Outcome does not bind command attempt.");
    }
    public static void ValidatePrevious(WaterBenchAuditEvent e,WaterBenchAuditEvent previous)
    {
        if(e.ConnectionId!=previous.ConnectionId||e.BootId!=previous.BootId||
            Parse(e.Payload).GetProperty("mask").GetInt32()!=Parse(previous.Payload).GetProperty("mask").GetInt32())
            throw AppException.Conflict("Cleaning scope mismatch.");
    }
}
