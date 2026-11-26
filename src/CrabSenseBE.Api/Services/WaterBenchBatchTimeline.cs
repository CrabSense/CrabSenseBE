using System.Text.Json;
using System.Text.RegularExpressions;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Api.Services;

// A firmware observation stream, separate from validated analytical results.
public sealed record BenchBatchEvent(Guid StationId,Guid EventId,Guid ConnectionId,string BootId,string BatchId,int Sequence,
    string Type,DateTimeOffset ReceivedAt,string? Mode=null,int? Mask=null,string? Analyte=null,string? SessionId=null,
    uint? ReactionStartMs=null,int? ReactionDurationMs=null,int? CaptureId=null,int? State=null,
    int? Started=null,int? Reacting=null,int? Imaged=null,int? Drained=null,Guid? RequestId=null,string? Reason=null);

public sealed class BenchObservedChannel(string analyte,int bit)
{
    public string Analyte {get;}=analyte;
    public int Bit {get;}=bit;
    public string? SessionId {get;set;}
    public uint? ReactionStartMs {get;set;}
    public int? ReactionDurationMs {get;set;}
    public int? CaptureId {get;set;}
    public string Status {get;set;}="waiting";
}

public sealed class WaterBenchBatchTimeline
{
    public const int MaxEvents=512;
    public static bool Hex16(string? value)=>value is not null&&Regex.IsMatch(value,@"\A[0-9a-f]{16}\z",RegexOptions.CultureInvariant);
    public static int Bit(string? analyte)=>analyte switch{"NO2"=>1,"NO3"=>2,"NH3"=>4,_=>0};
    public static int Duration(string? analyte)=>analyte switch{"NO2"=>300000,"NO3"=>120000,"NH3"=>600000,_=>0};
    public static string EvidenceKind(string type)=>type switch
    {
        "start"=>"batch_start","session"=>"session","status"=>"batch_status","image"=>"image_request",
        "reaction" or "interrupted"=>"operator_event",_=>throw AppException.BadRequest("Unsupported batch observation.")
    };
    public Guid StationId {get;private set;}
    public Guid ConnectionId {get;private set;}
    public string BootId {get;private set;}="";
    public string BatchId {get;private set;}="";
    public string Mode {get;private set;}="";
    public int Mask {get;private set;}
    public int LastSequence {get;private set;}
    public int Started {get;private set;}
    public int Reacting {get;private set;}
    public int Imaged {get;private set;}
    public int Drained {get;private set;}
    public string Status {get;private set;}="unobserved";
    public DateTimeOffset LastObservedAt {get;private set;}
    public Guid? RequestId {get;private set;}
    public List<BenchObservedChannel> Channels {get;}=[];
    public bool PhysicalCompletionVerified=>false;
    public bool ChemicalResultValidated=>false;
    public bool CleaningVerified=>false;

    private static void Require(bool condition,string message)
    {if(!condition)throw AppException.Conflict(message);}
    public static void ValidateEnvelope(BenchBatchEvent e)
    {
        if(e.StationId==Guid.Empty||e.EventId==Guid.Empty||e.ConnectionId==Guid.Empty||!Hex16(e.BootId)||!Hex16(e.BatchId)||
           e.Sequence is <1 or >MaxEvents||e.ReceivedAt==default||e.ReceivedAt>DateTimeOffset.UtcNow.AddMinutes(5)||
           e.RequestId==Guid.Empty||e.Reason?.Length>200)
            throw AppException.BadRequest("Invalid observation identity, sequence or time.");
        _=EvidenceKind(e.Type);
    }
    public void Apply(BenchBatchEvent e)
    {
        ValidateEnvelope(e);
        Require(e.Sequence==LastSequence+1,"Batch observation sequence is not contiguous.");
        if(LastSequence==0)
        {
            Require(e.Type=="start"&&e.Mode is "manual" or "scheduled"&&e.Mask is >=1 and <=7&&
                (e.Mode!="scheduled"||e.Mask==7),"Batch start required.");
            StationId=e.StationId;ConnectionId=e.ConnectionId;BootId=e.BootId;BatchId=e.BatchId;Mode=e.Mode!;Mask=e.Mask!.Value;
            RequestId=e.RequestId;Status="running_observed";
            foreach(var a in new[]{"NO2","NO3","NH3"})if((Mask&Bit(a))!=0)Channels.Add(new(a,Bit(a)));
        }
        else
        {
            Require(e.StationId==StationId&&e.ConnectionId==ConnectionId&&e.BootId==BootId&&e.BatchId==BatchId,"Batch identity mismatch.");
            Require(Status=="running_observed","Batch observation stream already terminal.");
            switch(e.Type)
            {
                case "session":
                {
                    var c=Channel(e);
                    Require(c.SessionId is null&&e.SessionId is not null&&
                        (c.Analyte=="NH3"?Hex16(e.SessionId):e.SessionId==BatchId+(c.Bit==1?"-1":"-2")),"Invalid or repeated session.");
                    c.SessionId=e.SessionId;c.Status="preparing_observed";break;
                }
                case "reaction":
                {
                    var c=Session(e);
                    Require(c.ReactionStartMs is null&&e.ReactionStartMs.HasValue&&e.ReactionDurationMs==Duration(c.Analyte),"Invalid reaction observation.");
                    c.ReactionStartMs=e.ReactionStartMs;c.ReactionDurationMs=e.ReactionDurationMs;c.Status="reacting_observed";break;
                }
                case "image":
                {
                    var c=Session(e);
                    Require(c.ReactionStartMs.HasValue&&c.CaptureId is null&&e.CaptureId is >=1 and <=65536&&
                        Channels.All(x=>x.CaptureId!=e.CaptureId),"Invalid or repeated image request.");
                    c.CaptureId=e.CaptureId;c.Status="capture_requested";break;
                }
                case "status":
                {
                    Require(e.Mask==Mask&&e.State is >=1 and <=3&&e.Started is >=0 and <=7&&e.Reacting is >=0 and <=7&&
                        e.Imaged is >=0 and <=7&&e.Drained is >=0 and <=7,"Invalid status masks.");
                    int started=e.Started!.Value,reacting=e.Reacting!.Value,imaged=e.Imaged!.Value,drained=e.Drained!.Value;
                    Require((started&~Mask)==0&&(reacting&~started)==0&&(imaged&~started)==0&&(drained&~imaged)==0&&
                        (reacting&imaged)==0&&(Started&~started)==0&&(Imaged&~imaged)==0&&(Drained&~drained)==0,"Inconsistent or regressed masks.");
                    Require(Channels.All(c=>((started&c.Bit)==0||c.SessionId!=null)&&
                        ((reacting&c.Bit)==0||c.ReactionStartMs.HasValue)&&((imaged&c.Bit)==0||c.CaptureId.HasValue)),"Status is missing prerequisite evidence.");
                    if(e.State==2)Require(started==Mask&&reacting==0&&imaged==Mask&&drained==Mask,"Incomplete terminal batch.");
                    Started=started;Reacting=reacting;Imaged=imaged;Drained=drained;
                    foreach(var c in Channels)
                    {
                        if((Drained&c.Bit)!=0)c.Status="drained_reported";
                        else if((Imaged&c.Bit)!=0)c.Status="image_confirmed_reported";
                        else if(c.CaptureId.HasValue)c.Status="capture_requested";
                        else if((Reacting&c.Bit)!=0)c.Status="reacting_observed";
                        else if(c.SessionId!=null)c.Status="preparing_observed";
                        if(e.State==3&&c.Status!="drained_reported")c.Status="fault_reported";
                    }
                    if(e.State==2)Status="firmware_complete_reported";
                    if(e.State==3)Status="fault_reported";
                    break;
                }
                case "interrupted":
                    Status="interrupted_observed";
                    foreach(var c in Channels.Where(c=>c.Status!="drained_reported"))c.Status="interrupted_observed";
                    break;
                default:throw AppException.Conflict("Unexpected observation type.");
            }
        }
        LastSequence=e.Sequence;LastObservedAt=e.ReceivedAt;
    }
    private BenchObservedChannel Channel(BenchBatchEvent e)=>Channels.SingleOrDefault(c=>c.Analyte==e.Analyte)
        ??throw AppException.Conflict("Analyte not selected.");
    private BenchObservedChannel Session(BenchBatchEvent e)
    {
        var c=Channel(e);Require(c.SessionId!=null&&c.SessionId==e.SessionId,"Session mismatch.");return c;
    }
    public static WaterBenchBatchTimeline Replay(IEnumerable<string> payloads)
    {
        var result=new WaterBenchBatchTimeline();
        foreach(var e in payloads.Select(x=>JsonSerializer.Deserialize<BenchBatchEvent>(x,WaterBatchPlanning.Json)
            ??throw AppException.Conflict("Invalid stored batch observation.")).OrderBy(x=>x.Sequence))result.Apply(e);
        return result;
    }
}
