using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Api.Services;

public sealed record Nh3Environment(decimal Ph,decimal TemperatureC,decimal SalinityPpt,DateTimeOffset SampleAt,
    DateTimeOffset PhAt,DateTimeOffset TemperatureAt,DateTimeOffset SalinityAt,int MaxAgeSeconds);
public static class WaterBenchAmmoniaPolicy
{
    public const int MaxAgeSeconds=10;
    public static Nh3Environment Environment(Nh3EvidenceEvent e,WaterMeasurement measurement,DateTimeOffset now)
    {
        WaterBenchNh3Policy.Validate(e);
        if(e.Type!="sample"||e.Sample is not {} s||e.ClockEstimate is not {} clock||
            s.Ph is not decimal ph||s.TemperatureC is not decimal temp||
            clock.EstimatedSampleUtc is not DateTimeOffset sampleAt||
            clock.EstimatedPhObservationUtc is not DateTimeOffset phAt||
            clock.EstimatedTemperatureObservationUtc is not DateTimeOffset tempAt||
            clock.AnchorRoundTripMs>1000||measurement.Unit!="ppt"||measurement.Value is <0 or >60||
            measurement.DataQuality!="Realtime"||!WaterBenchReceiptPolicy.IsDigest(measurement.EdgeEventKey)||
            measurement.MeasuredAt.Kind!=DateTimeKind.Utc||measurement.ReceivedAt is not DateTime received||
            received.Kind!=DateTimeKind.Utc||received<measurement.MeasuredAt.AddSeconds(-2)||
            received>measurement.MeasuredAt.AddSeconds(10))
            throw AppException.Conflict("Missing qualified NH3 environmental observations.");
        var salinityAt=new DateTimeOffset(measurement.MeasuredAt);
        // Conservative timing margin for the observed anchor RTT; this does not validate clock drift.
        var margin=TimeSpan.FromMilliseconds(clock.AnchorRoundTripMs);
        if(new[]{phAt,tempAt,salinityAt}.Any(t=>(t-sampleAt).Duration()+margin>TimeSpan.FromSeconds(MaxAgeSeconds))||
            new[]{sampleAt,phAt,tempAt,salinityAt}.Any(t=>t>now.AddSeconds(1))||received>now.UtcDateTime.AddSeconds(1))
            throw AppException.Conflict("Environmental readings are not time-aligned.");
        return new(ph,temp,measurement.Value,sampleAt,phAt,tempAt,salinityAt,MaxAgeSeconds);
    }
    public static void Method(string method,string unit,Nh3Environment env)
    {
        if(method=="printed_table")
        {
            if(unit!="mg/L NH4")throw AppException.BadRequest("Printed table requires an explicitly confirmed NH4 chart basis.");
            return;
        }
        if(method!="equilibrium"||unit is not ("mg/L NH4" or "mg/L TAN-N" or "mg/L NH3"))
            throw AppException.BadRequest("Unsupported ammonia method or measurand.");
        if(unit=="mg/L NH3")throw AppException.BadRequest("Already free NH3; no conversion required.");
        if(env.Ph is <6 or >9.5m||env.TemperatureC is <10 or >35||env.SalinityPpt is <1 or >35)
            throw AppException.BadRequest("Outside the supported saline equilibrium domain.");
    }
}
