using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Api.Services;

public sealed record Nh3SampleReading(uint SampleUptimeMs,uint PhObservedUptimeMs,decimal? Ph,
    uint TemperatureObservedUptimeMs,decimal? TemperatureC,int TemperatureRegisterMaxAgeMs);
public sealed record Nh3ImageObservation(int CaptureId,uint RequestedUptimeMs,uint ReactionFinishedUptimeMs);
public sealed record Nh3ClockEstimate(DateTimeOffset? EstimatedSampleUtc,DateTimeOffset? EstimatedPhObservationUtc,
    DateTimeOffset? EstimatedTemperatureObservationUtc,long AnchorRoundTripMs);
public sealed record Nh3EvidenceEvent(Guid StationId,Guid EventId,Guid ConnectionId,string BootId,string BatchId,
    string SessionId,string Type,DateTimeOffset ReceivedAt,string MasterHardwareId,string SlaveHardwareId,
    Nh3SampleReading? Sample=null,Nh3ImageObservation? Image=null,Nh3ClockEstimate? ClockEstimate=null);
public static class WaterBenchNh3Policy
{
    public static uint Elapsed(uint later,uint earlier)=>unchecked(later-earlier);
    public static void Validate(Nh3EvidenceEvent e)
    {
        if(e.StationId==Guid.Empty||e.EventId==Guid.Empty||e.ConnectionId==Guid.Empty||
            !WaterBenchBatchTimeline.Hex16(e.BootId)||!WaterBenchBatchTimeline.Hex16(e.BatchId)||!WaterBenchBatchTimeline.Hex16(e.SessionId)||
            !WaterBenchConfiguration.HardwareId(e.MasterHardwareId)||!WaterBenchConfiguration.HardwareId(e.SlaveHardwareId)||
            e.MasterHardwareId==e.SlaveHardwareId||e.ReceivedAt==default||e.ReceivedAt>DateTimeOffset.UtcNow.AddMinutes(5))
            throw AppException.BadRequest("Invalid NH3 evidence identity.");
        if(e.Type=="sample")
        {
            var s=e.Sample;
            if(s is null||e.Image!=null||s.TemperatureRegisterMaxAgeMs!=3000||
                s.Ph is <0 or >14||s.TemperatureC is <-10 or >60||
                (s.Ph.HasValue&&Elapsed(s.PhObservedUptimeMs,s.SampleUptimeMs)>1000)||
                (s.TemperatureC.HasValue&&Elapsed(s.TemperatureObservedUptimeMs,s.SampleUptimeMs)>1000))
                throw AppException.BadRequest("Invalid NH3 sample observation.");
            if(e.ClockEstimate is {} c)
            {
                bool ValidTime(DateTimeOffset? t)=>!t.HasValue||
                    (t.Value>=e.ReceivedAt.AddMinutes(-31)&&t.Value<=e.ReceivedAt.AddSeconds(5));
                if(c.AnchorRoundTripMs is <0 or >3000||!ValidTime(c.EstimatedSampleUtc)||
                    !ValidTime(c.EstimatedPhObservationUtc)||!ValidTime(c.EstimatedTemperatureObservationUtc)||
                    (!s.Ph.HasValue&&c.EstimatedPhObservationUtc.HasValue)||
                    (!s.TemperatureC.HasValue&&c.EstimatedTemperatureObservationUtc.HasValue))
                    throw AppException.BadRequest("Invalid estimated clock evidence.");
                bool Matches(DateTimeOffset? t,uint observed)=>!t.HasValue||!c.EstimatedSampleUtc.HasValue||
                    Math.Abs((t.Value-c.EstimatedSampleUtc.Value).TotalMilliseconds-(long)Elapsed(observed,s.SampleUptimeMs))<=1;
                if(!Matches(c.EstimatedPhObservationUtc,s.PhObservedUptimeMs)||
                    !Matches(c.EstimatedTemperatureObservationUtc,s.TemperatureObservedUptimeMs))
                    throw AppException.BadRequest("NH3 estimated clock mismatch.");
            }
        }
        else if(e.Type=="image_link")
        {
            if(e.Sample!=null||e.ClockEstimate!=null||e.Image is not {} i||i.CaptureId is <1 or >65536||
                Elapsed(i.RequestedUptimeMs,i.ReactionFinishedUptimeMs)>1800000)
                throw AppException.BadRequest("Invalid NH3 image observation.");
        }
        else throw AppException.BadRequest("Unsupported NH3 evidence kind.");
    }
    public static void Match(Nh3EvidenceEvent e,WaterBenchBatchTimeline timeline,BenchBatchEvent session,BenchBatchEvent? image)
    {
        var c=timeline.Channels.SingleOrDefault(x=>x.Analyte=="NH3");
        if(timeline.StationId!=e.StationId||timeline.ConnectionId!=e.ConnectionId||
            timeline.BootId!=e.BootId||timeline.BatchId!=e.BatchId||c is null||c.SessionId!=e.SessionId||
            session.Type!="session"||session.SessionId!=e.SessionId||session.Analyte!="NH3"||
            session.StationId!=e.StationId||session.ConnectionId!=e.ConnectionId||session.BootId!=e.BootId||
            session.BatchId!=e.BatchId||e.ReceivedAt<session.ReceivedAt.AddSeconds(-2))
            throw AppException.Conflict("NH3 session mismatch.");
        if(e.Sample is {} s&&c.ReactionStartMs is uint start&&Elapsed(start,s.SampleUptimeMs)>1800000)
            throw AppException.Conflict("NH3 sample is outside reaction window.");
        if(e.Image is {} i&&(image is null||image.Type!="image"||image.Analyte!="NH3"||
            image.SessionId!=e.SessionId||image.StationId!=e.StationId||image.ConnectionId!=e.ConnectionId||
            image.BootId!=e.BootId||image.BatchId!=e.BatchId||image.CaptureId!=i.CaptureId||
            c.CaptureId!=i.CaptureId||c.ReactionStartMs is not uint r||
            Elapsed(i.ReactionFinishedUptimeMs,r)<600000||Elapsed(i.RequestedUptimeMs,r)>1800000||
            Math.Abs((e.ReceivedAt-image.ReceivedAt).TotalSeconds)>2))
            throw AppException.Conflict("NH3 image request mismatch.");
    }
}
