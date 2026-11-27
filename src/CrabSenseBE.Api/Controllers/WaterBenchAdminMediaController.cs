using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/areas/{areaId:guid}/water-bench/admin-media")]
[Authorize(Roles=AppRoles.FarmManage)]
public sealed class WaterBenchAdminMediaController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid areaId,[FromQuery] Guid? cameraId,[FromQuery] int skip=0,[FromQuery] int take=50,CancellationToken ct=default)
    {
        await new FarmAreaScope(uow,user).EnsureAsync(areaId,true,ct);
        if(skip<0||take is <1 or >100)throw AppException.BadRequest("Invalid page.");
        Response.Headers["Cache-Control"]="no-store";
        var cameras=await db.Devices.AsNoTracking().Where(d=>d.FarmingAreaId==areaId&&d.DeviceType=="camera")
            .Select(d=>new{d.Id,d.DeviceCode}).OrderBy(d=>d.DeviceCode).ToListAsync(ct);
        var q=from a in db.MediaAssets.AsNoTracking() join d in db.Devices.AsNoTracking() on a.DeviceId equals (Guid?)d.Id
            where d.FarmingAreaId==areaId&&d.DeviceType=="camera"&&(!cameraId.HasValue||d.Id==cameraId)&&a.Category=="image"&&!a.IsShared&&
                a.SizeBytes>0&&a.SizeBytes<=5*1024*1024&&a.BoxId==null&&a.CrabId==null&&a.RelatedEntityType=="device"&&a.RelatedEntityId==d.Id&&
                (a.ContentType=="image/jpeg"||a.ContentType=="image/png")&&(a.Notes==null||!a.Notes.StartsWith("water-bench:"))
            select a;
        var items=await q.OrderByDescending(a=>a.CreatedAt).ThenBy(a=>a.Id).Skip(skip).Take(take)
            .Select(a=>new{a.Id,a.DeviceId,a.FileName,a.ContentType,a.SizeBytes,a.CreatedAt}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{cameras,items,total=await q.CountAsync(ct),skip,take}));
    }
}
