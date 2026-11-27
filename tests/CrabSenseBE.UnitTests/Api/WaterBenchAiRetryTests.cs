using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.UnitTests.Api;
public sealed class WaterBenchAiRetryTests
{
    [Fact]
    public void DurableLeaseBackoffAndFencing()
    {
        var now=DateTime.UtcNow;var id=Guid.NewGuid();var hash=new string('a',64);
        var a=new WaterBenchAiAttempt{RequestId=id,RequestHash=hash,Number=1,LeaseUntil=now.AddSeconds(5)};
        Assert.Equal("inference_running",WaterBenchAiRetry.Status(a,now));
        Assert.True(WaterBenchAiRetry.Owns(a,a.Id,now));
        Assert.Throws<AppException>(()=>WaterBenchAiRetry.EnsureRetry(a,id,hash,now));
        Assert.False(WaterBenchAiRetry.Owns(a,a.Id,now.AddSeconds(6)));
        WaterBenchAiRetry.EnsureRetry(a,id,hash,now.AddSeconds(6));
        Assert.Throws<AppException>(()=>WaterBenchAiRetry.EnsureRetry(a,Guid.NewGuid(),hash,now.AddSeconds(6)));
        a.State="retryable";a.RetryAfter=now.AddSeconds(10);
        Assert.Equal("retry_backoff",WaterBenchAiRetry.Status(a,now));
        WaterBenchAiRetry.EnsureRetry(a,id,hash,now.AddSeconds(11));
        a.Number=3;Assert.Equal("retry_exhausted",WaterBenchAiRetry.Status(a,now.AddSeconds(11)));
        Assert.Throws<AppException>(()=>WaterBenchAiRetry.EnsureRetry(a,id,hash,now.AddSeconds(11)));
    }
    [Theory]
    [InlineData(400,"retest_required")]
    [InlineData(503,"retryable")]
    [InlineData(502,"contract_blocked")]
    [InlineData(409,"contract_blocked")]
    public void FailuresAreSeparated(int status,string state)=>Assert.Equal(state,WaterBenchAiRetry.FailureState(status));
}
