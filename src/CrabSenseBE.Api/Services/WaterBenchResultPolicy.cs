using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrabSenseBE.Api.Services;

public static class WaterBenchResultPolicy
{
    public static bool ValidUnit(string analyte,string unit)=>analyte switch
    {
        "NO2"=>unit=="mg/L NO2",
        "NO3"=>unit=="mg/L NO3",
        "NH3"=>unit is "mg/L NH3" or "mg/L NH4" or "mg/L TAN-N",
        _=>false
    };
    public static string Marker(Guid station,string boot,string batch,string analyte,int capture)=>
        $"water-bench:{station:N}:{boot}:{batch}:{analyte}:{capture}";
    public static bool ReadyForEstimate(WaterBenchBatchTimeline timeline,BenchObservedChannel channel)=>
        timeline.Status=="firmware_complete_reported"&&channel.Status=="drained_reported"&&
        channel.SessionId is not null&&channel.CaptureId.HasValue&&channel.ReactionStartMs.HasValue&&
        channel.ReactionDurationMs==WaterBenchBatchTimeline.Duration(channel.Analyte);
    public static void Conditions(WaterColorCalibration profile,string analyte,DateTime observed,DateTime now,
        string lot,int seconds,decimal salinity)
    {
        if(profile.Analyte!=analyte||!ValidUnit(analyte,profile.Unit)||profile.CreatedAt>observed||
           profile.RevokedAt.HasValue||profile.ValidUntil<=now||profile.ValidUntil<=observed||
           profile.QualityCheckDueAt<=now||profile.QualityCheckDueAt<=observed||
           profile.ReagentLot!=lot||!string.Equals(profile.KitName,WaterBatchPlanning.Kit(analyte),StringComparison.OrdinalIgnoreCase)||
           !WaterAssayCatalog.Compatible(profile)||!WaterAiClient.Supported(profile.AiAlgorithm)||
           string.IsNullOrWhiteSpace(profile.AiCalibrationToken)||string.IsNullOrWhiteSpace(profile.AiCalibrationId))
            throw AppException.Conflict("Hiệu chuẩn AI, bộ kit, lô thuốc hoặc đơn vị không hợp lệ cho phiên.");
        var p=JsonSerializer.Deserialize<ColorProtocol>(profile.ProtocolJson,WaterBatchPlanning.Json)
            ??throw AppException.Conflict("Thiếu protocol.");
        if(seconds<p.ReactionSeconds||Math.Abs((long)seconds-p.ReactionSeconds)>p.TimingToleranceSeconds||
            salinity<p.MinSalinityPpt||salinity>p.MaxSalinityPpt)
            throw AppException.BadRequest("Thời gian đọc màu hoặc độ mặn ngoài phạm vi hiệu chuẩn.");
    }
    // P2.2.5 writes this receipt only after validating bytes and binding to the same capture.
    // No public API may accept arbitrary jpeg evidence or user-supplied media IDs as trusted linkage.
    public record CaptureReceipt(Guid MediaId,string Sha256,string Contract,Guid? CalibrationId=null,Guid? CameraId=null,int? ConfigurationVersion=null);
    public static CaptureReceipt Receipt(string payload)
    {
        var r=JsonSerializer.Deserialize<CaptureReceipt>(payload,WaterBatchPlanning.Json);
        if(r is null||r.MediaId==Guid.Empty||r.Contract!="water-bench-media-v1"||
           r.Sha256 is null||!Regex.IsMatch(r.Sha256,@"\A[0-9a-f]{64}\z",RegexOptions.CultureInvariant))
            throw AppException.Conflict("Thiếu biên nhận ảnh đã liên kết.");
        return r;
    }
}
