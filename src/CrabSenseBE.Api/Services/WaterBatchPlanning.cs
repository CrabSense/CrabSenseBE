using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Services;

public static class WaterBatchPlanning
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public record Target(string Analyte, Guid CalibrationId);
    public static readonly string[] Sources = ["RAS_RETURN","RETURN_TANK","BIO_TANK","SETTLING_TANK","INLET"];

    public static Target[] Normalize(Target[]? input,bool scheduled)
    {
        if(input is null || input.Length==0 || input.Length>3 || input.Any(x=>x is null))
            throw AppException.BadRequest("Chọn từ 1 đến 3 chỉ tiêu.");
        var targets=input.Select(x=>new Target(x.Analyte?.Trim().ToUpperInvariant()??"",x.CalibrationId)).ToArray();
        if(targets.Any(x=>x.Analyte is not ("NO2" or "NO3" or "NH3") || x.CalibrationId==Guid.Empty) ||
           targets.Select(x=>x.Analyte).Distinct().Count()!=targets.Length ||
           targets.Select(x=>x.CalibrationId).Distinct().Count()!=targets.Length || (scheduled&&targets.Length!=3))
            throw AppException.BadRequest("Lịch định kỳ phải có đúng NO2, NO3, NH3; mỗi chỉ tiêu một hiệu chuẩn.");
        return targets;
    }
    public static string Source(string? source)
    {
        var value=source?.Trim().ToUpperInvariant();
        if(!Sources.Contains(value)) throw AppException.BadRequest("Nguồn mẫu không hợp lệ.");
        return value!;
    }
    public static string Kit(string analyte)=>analyte switch { "NO2"=>"Sera","NO3"=>"VASTOCEAN","NH3"=>"Thủy Phước",_=>"" };
    public static async Task<bool> ProfilesReady(AppDbContext db,Guid areaId,Target[] targets,DateTime now,CancellationToken ct)
    {
        var ids=targets.Select(x=>x.CalibrationId).ToArray();
        var profiles=await db.WaterColorCalibrations.AsNoTracking().Where(x=>x.FarmingAreaId==areaId&&ids.Contains(x.Id)&&x.RevokedAt==null&&x.ValidUntil>now).ToListAsync(ct);
        return targets.All(t=>profiles.Any(p=>p.Id==t.CalibrationId&&p.Analyte==t.Analyte&&string.Equals(p.KitName,Kit(t.Analyte),StringComparison.OrdinalIgnoreCase)));
    }
    public static DateTime Next(DateTime due,DateTime now,int intervalMinutes)
    {
        var interval=TimeSpan.FromMinutes(intervalMinutes);
        var elapsed=Math.Max(0L,(now-due).Ticks);
        return due.AddTicks(checked((elapsed/interval.Ticks+1)*interval.Ticks));
    }
    public static WaterAnalysisBatch New(Guid areaId,string trigger,Target[] targets,string source,DateTime now,
        Guid? requestedBy=null,Guid? scheduleId=null,DateTime? scheduledFor=null)=>new()
        {
            FarmingAreaId=areaId,Trigger=trigger,Status="waiting_samples",
            TargetsJson=JsonSerializer.Serialize(targets,Json),SampleSource=source,
            RequestedBy=requestedBy,ScheduleId=scheduleId,ScheduledFor=scheduledFor,CreatedAt=now
        };
}
