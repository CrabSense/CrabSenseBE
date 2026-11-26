using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Services;

public static class WaterBenchConfiguration
{
    public record Settings(int ExpectedVersion,bool Enabled,string[] Times,int GraceMinutes,WaterBatchPlanning.Target[] Targets,string SampleSource);
    public static bool HardwareId(string? id)=>id is not null&&Regex.IsMatch(id,@"\A[0-9a-f]{12}\z",RegexOptions.CultureInvariant);
    public static string[] Times(string[]? input,bool enabled,int grace)
    {
        if(input is null||input.Length>8||(enabled&&input.Length==0)||grace is <1 or >15||
            input.Any(t=>t is null||!TimeOnly.TryParseExact(t,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _))||
            input.Distinct(StringComparer.Ordinal).Count()!=input.Length)
            throw AppException.BadRequest("Chọn 1–8 giờ HH:mm khác nhau, cửa sổ 1–15 phút.");
        var sorted=input.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        var minutes=sorted.Select(t=>{var x=TimeOnly.ParseExact(t,"HH:mm",CultureInfo.InvariantCulture);return x.Hour*60+x.Minute;}).ToArray();
        if(minutes.Length>1)
            for(int i=0;i<minutes.Length;i++)
                if((minutes[(i+1)%minutes.Length]-minutes[i]+1440)%1440<grace)
                    throw AppException.BadRequest("Các cửa sổ xác nhận không được trùng nhau.");
        return sorted;
    }
    public static IQueryable<WaterBenchStation> Active(AppDbContext db,Guid areaId)=>
        from station in db.WaterBenchStations
        join kiosk in db.FarmKiosks on station.KioskId equals kiosk.Id
        where kiosk.FarmingAreaId==areaId&&station.RevokedAt==null
        select station;
    // Serialize all area scheduler ownership changes inside a PostgreSQL transaction.
    public static Task LockArea(AppDbContext db,Guid areaId,CancellationToken ct)=>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"FarmingAreas\" WHERE \"Id\"={areaId} FOR UPDATE",ct);
    public static object View(WaterBenchStation station,bool profilesReady)=>new {
        stationId=station.Id,station.KioskId,station.MasterHardwareId,station.SlaveHardwareId,station.ConfigurationVersion,
        enabled=station.RevokedAt==null&&station.ScheduleEnabled&&profilesReady,
        configuredEnabled=station.ScheduleEnabled,profilesReady,station.RevokedAt,
        times=JsonSerializer.Deserialize<string[]>(station.DailyTimesJson,WaterBatchPlanning.Json),
        station.GraceMinutes,targets=JsonSerializer.Deserialize<WaterBatchPlanning.Target[]>(station.TargetsJson,WaterBatchPlanning.Json),
        station.SampleSource,timezone="UTC+07:00",execution="kiosk_supervised",physicalDispatch=false
    };
    public static async Task<bool> Ready(AppDbContext db,WaterBenchStation station,Guid areaId,CancellationToken ct)
    {
        if(station.RevokedAt!=null||station.ConfigurationVersion==0)return false;
        var targets=JsonSerializer.Deserialize<WaterBatchPlanning.Target[]>(station.TargetsJson,WaterBatchPlanning.Json);
        return targets is {Length:3}&&await WaterBatchPlanning.ProfilesReady(db,areaId,targets,DateTime.UtcNow,ct);
    }
}
