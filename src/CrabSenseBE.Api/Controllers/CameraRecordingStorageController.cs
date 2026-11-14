using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles=AppRoles.Platform)]
[Route("api/cameras/recording-storage")]
public sealed class CameraRecordingStorageController(AppDbContext db):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid? farmingAreaId,int page=1,int pageSize=50,CancellationToken ct=default)
    {
        if(page<1||pageSize is <1 or >200||(long)(page-1)*pageSize>int.MaxValue)throw AppException.BadRequest("Invalid pagination.");
        var cameras=CameraRecordingLibrary.CameraDevices(db);
        if(farmingAreaId.HasValue)cameras=cameras.Where(x=>x.FarmingAreaId==farmingAreaId);
        var totalCount=await cameras.CountAsync(ct);
        var rows=await cameras.OrderBy(x=>x.DeviceCode).ThenBy(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        var ids=rows.Select(x=>x.Id).ToArray();
        var assignments=await (from a in db.CameraScheduleAssignments.AsNoTracking()
            join s in db.CameraSchedules.AsNoTracking() on a.ScheduleId equals s.Id
            where ids.Contains(a.DeviceId)&&!s.Deleted select new{a.DeviceId,Schedule=s}).ToDictionaryAsync(x=>x.DeviceId,ct);
        var media=CameraRecordingLibrary.TechnicalVideos(db);
        var usage=await media.Where(x=>x.DeviceId.HasValue&&ids.Contains(x.DeviceId.Value)).GroupBy(x=>x.DeviceId!.Value)
            .Select(g=>new{DeviceId=g.Key,Bytes=g.Sum(x=>x.SizeBytes),Files=g.Count()}).ToDictionaryAsync(x=>x.DeviceId,ct);
        var items=new List<object>();
        foreach(var camera in rows)
        {
            assignments.TryGetValue(camera.Id,out var assignment);usage.TryGetValue(camera.Id,out var used);
            var schedule=assignment?.Schedule;var bytes=used?.Bytes??0L;
            long expiredBytes=0;int expiredCount=0;
            if(schedule is not null)
            {
                var cutoff=DateTime.UtcNow.AddDays(-schedule.RetentionDays);
                var expired=media.Where(m=>m.DeviceId==camera.Id&&!m.IsShared&&db.CameraRecordingExecutions.Any(e=>e.ScheduleId==schedule.Id&&e.DeviceId==camera.Id&&e.Status=="Completed"&&e.MediaAssetId==m.Id&&e.EndedAt<cutoff));
                expiredCount=await expired.CountAsync(ct);expiredBytes=await expired.SumAsync(x=>(long?)x.SizeBytes,ct)??0;
            }
            var limit=schedule is null?null:CameraRecordingRetentionService.LimitBytes(schedule);
            items.Add(new{id=camera.Id,deviceId=camera.Id,camera.DeviceCode,camera.Name,camera.FarmingAreaId,
                scheduleId=schedule?.Id,scheduleName=schedule?.Name,retentionDays=schedule?.RetentionDays,autoDeleteExpired=schedule?.AutoDeleteExpired??false,
                usedBytes=bytes,limitBytes=limit,files=used?.Files??0,expiredBytes,expiredCount,quotaReached=limit.HasValue&&bytes>=limit.Value});
        }
        return Ok(ApiResponse<object>.Ok(new{items,totalCount,page,pageSize}));
    }
}
