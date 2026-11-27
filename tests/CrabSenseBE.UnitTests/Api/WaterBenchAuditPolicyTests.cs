using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchAuditPolicyTests
{
    [Fact]
    public void StableIdentityAndExactAttemptBinding()
    {
        var station=Guid.NewGuid();var connection=Guid.NewGuid();var id=Guid.NewGuid();
        var at=DateTimeOffset.UtcNow;const string boot="0123456789abcdef";
        var payload=JsonSerializer.Serialize(new{version=1,stationId=station,connectionId=connection,bootId=boot,at,
            requestId=id,sequence=1,state="send_attempt",request=new{requestId=id}});
        var a=new WaterBenchAuditEvent(station,WaterBenchAuditPolicy.EventId(station,"command",id,0),connection,boot,"command",id,0,at,payload);
        WaterBenchAuditPolicy.Validate(a);
        Assert.Throws<AppException>(()=>WaterBenchAuditPolicy.Validate(a with{EventId=Guid.NewGuid()}));
        var outcome=JsonSerializer.Serialize(new{version=1,connectionId=connection,bootId=boot,at,requestId=id,sequence=1,
            state="unknown",attemptSha256=WaterBenchAuditPolicy.Hash(payload)});
        var o=new WaterBenchAuditEvent(station,WaterBenchAuditPolicy.EventId(station,"command_outcome",id,0),connection,boot,
            "command_outcome",id,0,at,outcome);
        WaterBenchAuditPolicy.Validate(o);WaterBenchAuditPolicy.ValidateOutcome(o,a);
        Assert.Throws<AppException>(()=>WaterBenchAuditPolicy.ValidateOutcome(o,a with{Payload=payload+" "}));
    }
}
