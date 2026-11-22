using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;

namespace CrabSenseBE.Api.Services;

/// <summary>Server-side progression based on operator attestations. Never emits physical commands.</summary>
public static class WaterAssayFlow
{
    public record Event(string Step,string Action,DateTime At,Guid? Actor,string? Detail);
    public static bool Terminal(WaterAssayChannel c)=>c.State is "completed" or "failed" or "cancelled";
    public static string Current(WaterAssayChannel c)=>Terminal(c)?"":WaterAssayCatalog.Snapshot(c).Steps[c.StepIndex].Code;
    private static void Log(WaterAssayChannel c,string step,string action,DateTime now,Guid? actor,string? detail=null)
    {
        var events=JsonSerializer.Deserialize<List<Event>>(c.EventsJson,WaterBatchPlanning.Json)??[];
        events.Add(new(step,action,now,actor,detail));
        c.EventsJson=JsonSerializer.Serialize(events,WaterBatchPlanning.Json);
        c.UpdatedAt=now;
    }
    private static void Advance(WaterAssayChannel c,DateTime now)
    {
        c.StepIndex++;c.StepStartedAt=now;c.State="in_progress";
        if(Current(c)=="wait")
            c.ReactionReadyAt=now.AddSeconds(WaterAssayCatalog.Snapshot(c).ReactionSeconds);
    }
    public static void Confirm(WaterAssayChannel c,string step,DateTime now,Guid actor,Guid? mediaId=null)
    {
        if(Terminal(c)||Current(c)!=step||step=="analyze")
            throw AppException.Conflict("Bước không hợp lệ; tải lại trạng thái.");
        var profile=WaterAssayCatalog.Snapshot(c);
        var protocol=JsonSerializer.Deserialize<ColorProtocol>(c.ProtocolJson,WaterBatchPlanning.Json)!;
        var definition=profile.Steps[c.StepIndex];
        if(step=="wait"||step=="capture")
        {
            if(c.ReactionReadyAt is not DateTime ready || now<ready)
                throw AppException.Conflict("Chưa đủ thời gian phản ứng.");
            if(now>ready.AddSeconds(protocol.TimingToleranceSeconds))
                throw AppException.Conflict("Đã quá cửa sổ đọc màu; báo lỗi và làm sạch để đo lại.");
        }
        else if(definition.Seconds is int seconds && now<c.StepStartedAt.AddSeconds(seconds))
            throw AppException.Conflict("Chưa đủ thời gian thao tác.");
        if(step=="capture")
        {
            if(mediaId is not Guid id||id==Guid.Empty)throw AppException.BadRequest("Cần ảnh đã tải lên.");
            c.CapturedMediaId=id;
        }
        else if(mediaId.HasValue)throw AppException.BadRequest("Chỉ bước chụp ảnh nhận MediaId.");
        Log(c,step,"operator_confirmed",now,actor,step=="rinse"?"kit_instructions_attested":mediaId?.ToString());
        if(step=="cleanup")
        {
            c.State=c.Failure is null?"completed":"failed";
            c.StepStartedAt=now;
        }
        else Advance(c,now);
    }
    public static void Fail(WaterAssayChannel c,string step,string reason,DateTime now,Guid actor)
    {
        if(Terminal(c)||Current(c)!=step||step=="cleanup"||string.IsNullOrWhiteSpace(reason)||reason.Length>500)
            throw AppException.Conflict("Không thể báo lỗi ở bước này hoặc thiếu lý do.");
        c.Failure=reason.Trim();Log(c,step,"operator_reported_failure",now,actor,c.Failure);
        c.StepIndex=WaterAssayCatalog.Snapshot(c).Steps.Length-1;c.StepStartedAt=now;c.State="cleanup_required";
    }
    public static void ResultSaved(WaterAssayChannel c,Guid mediaId,DateTime now,Guid actor)
    {
        if(Current(c)!="analyze"||c.CapturedMediaId!=mediaId||c.Failure is not null)
            throw AppException.Conflict("Ảnh chưa được xác nhận ở bước chụp của kênh.");
        Log(c,"analyze","result_saved",now,actor,mediaId.ToString());
        Advance(c,now);c.State="cleanup_required";
    }
    public static string BatchStatus(IEnumerable<WaterAssayChannel> channels)
    {
        var all=channels.ToArray();
        if(all.Length==0||all.Any(c=>!Terminal(c)))return "waiting_samples";
        return all.All(c=>c.State=="completed")?"completed":all.All(c=>c.State=="failed")?"failed":"partial";
    }
}
