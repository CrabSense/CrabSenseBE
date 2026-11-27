using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
namespace CrabSenseBE.Api.Services;

public static class WaterBenchAiRetry
{
    public const int MaxAttempts=3;
    public static readonly TimeSpan Lease=TimeSpan.FromMinutes(2);
    public static string FailureState(int status)=>status==503?"retryable":status==400?"retest_required":"contract_blocked";
    public static string Status(WaterBenchAiAttempt? last,DateTime now)
    {
        if(last==null)return "not_attempted";
        if(last.State=="running"&&last.LeaseUntil>now)return "inference_running";
        if(last.State=="succeeded")return "completed";
        if(last.State=="retest_required")return "retest_recommended";
        if(last.State=="contract_blocked")return "technical_review_required";
        if(last.Number>=MaxAttempts)return "retry_exhausted";
        if(last.State=="retryable"&&last.RetryAfter>now)return "retry_backoff";
        return "retry_available";
    }
    public static void EnsureRetry(WaterBenchAiAttempt? last,Guid requestId,string hash,DateTime now)
    {
        if(last==null)return;
        if(last.RequestId!=requestId||last.RequestHash!=hash)throw AppException.Conflict("Retry must reuse original request ID and body.");
        if(Status(last,now)!="retry_available")throw AppException.Conflict("AI attempt cannot be retried: "+Status(last,now));
    }
    public static bool Owns(WaterBenchAiAttempt? last,Guid id,DateTime now)=>
        last!=null&&last.Id==id&&last.State=="running"&&last.LeaseUntil>now;
    public static TimeSpan Delay(int number)=>TimeSpan.FromSeconds(number==1?5:15);
}
