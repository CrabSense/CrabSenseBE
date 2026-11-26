using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/areas/{areaId:guid}/water-bench")]
[Authorize(Roles=AppRoles.FarmWrite)]
public sealed class WaterBenchConfigurationController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    private readonly FarmAreaScope scope=new(uow,user);
    public record RegisterRequest(Guid KioskId,string MasterHardwareId,string SlaveHardwareId);
    [HttpGet("station")]
    public async Task<IActionResult> Get(Guid areaId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var row=await WaterBenchConfiguration.Active(db,areaId).AsNoTracking().SingleOrDefaultAsync(ct);
        return Ok(ApiResponse<object?>.Ok(row is null?null:WaterBenchConfiguration.View(row,await WaterBenchConfiguration.Ready(db,row,areaId,ct))));
    }
    [HttpPost("station")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Register(Guid areaId,RegisterRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        if(!WaterBenchConfiguration.HardwareId(request.MasterHardwareId)||!WaterBenchConfiguration.HardwareId(request.SlaveHardwareId)||
            request.MasterHardwareId==request.SlaveHardwareId)throw AppException.BadRequest("Hai hardware ID phải khác nhau, 12 ký tự hex thường.");
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await WaterBenchConfiguration.LockArea(db,areaId,ct);
        if(!await db.FarmKiosks.AnyAsync(x=>x.Id==request.KioskId&&x.FarmingAreaId==areaId&&x.Status!="Revoked",ct)||
            !await db.KioskCredentials.AnyAsync(x=>x.KioskId==request.KioskId&&x.RevokedAt==null,ct))
            throw AppException.BadRequest("Kiosk chưa được cấp phát hoặc không thuộc khu.");
        if(await WaterBenchConfiguration.Active(db,areaId).AnyAsync(ct))throw AppException.Conflict("Khu đã có trạm phân tích.");
        if(await db.WaterAnalysisSchedules.AnyAsync(x=>x.FarmingAreaId==areaId&&x.Enabled,ct)||
            await db.WaterAnalysisBatches.AnyAsync(x=>x.FarmingAreaId==areaId&&x.Status=="waiting_samples",ct)||
            await db.WaterAnalysisRuns.AnyAsync(x=>x.FarmingAreaId==areaId&&(x.Status=="running"||x.Status=="stopping"),ct))
            throw AppException.Conflict("Tắt lịch cũ và kết thúc phiên đang chạy trước khi đăng ký trạm.");
        var row=new WaterBenchStation{KioskId=request.KioskId,MasterHardwareId=request.MasterHardwareId,SlaveHardwareId=request.SlaveHardwareId};
        db.WaterBenchStations.Add(row);
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException){throw AppException.Conflict("Kiosk hoặc hardware ID đã được đăng ký.");}
        return Ok(ApiResponse<object>.Ok(WaterBenchConfiguration.View(row,false)));
    }
    [HttpPut("configuration")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Configure(Guid areaId,WaterBenchConfiguration.Settings request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        var times=WaterBenchConfiguration.Times(request.Times,request.Enabled,request.GraceMinutes);
        var targets=WaterBatchPlanning.Normalize(request.Targets,true);
        var source=WaterBatchPlanning.Source(request.SampleSource);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await WaterBenchConfiguration.LockArea(db,areaId,ct);
        var row=await WaterBenchConfiguration.Active(db,areaId).SingleOrDefaultAsync(ct)??throw AppException.NotFound("Water bench station");
        if(row.ConfigurationVersion!=request.ExpectedVersion||row.ConfigurationVersion==int.MaxValue)
            throw AppException.Conflict("Phiên bản cấu hình đã thay đổi; tải lại.");
        var ready=await WaterBatchPlanning.ProfilesReady(db,areaId,targets,DateTime.UtcNow,ct);
        if(request.Enabled&&(!ready||!await db.FarmKiosks.AnyAsync(x=>x.Id==row.KioskId&&x.Status!="Revoked",ct)))
            throw AppException.Conflict("Kiosk hoặc hiệu chuẩn 3 chất chưa sẵn sàng.");
        row.ScheduleEnabled=request.Enabled;row.DailyTimesJson=JsonSerializer.Serialize(times,WaterBatchPlanning.Json);
        row.TargetsJson=JsonSerializer.Serialize(targets,WaterBatchPlanning.Json);row.SampleSource=source;
        row.GraceMinutes=request.GraceMinutes;row.ConfigurationVersion++;row.UpdatedAt=DateTime.UtcNow;
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateConcurrencyException){throw AppException.Conflict("Cấu hình vừa thay đổi; tải lại.");}
        return Ok(ApiResponse<object>.Ok(WaterBenchConfiguration.View(row,ready)));
    }
    [HttpPost("station/{stationId:guid}/revoke")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Revoke(Guid areaId,Guid stationId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await WaterBenchConfiguration.LockArea(db,areaId,ct);
        var row=await WaterBenchConfiguration.Active(db,areaId).SingleOrDefaultAsync(x=>x.Id==stationId,ct)
            ??throw AppException.NotFound("Water bench station");
        if(row.ConfigurationVersion==int.MaxValue)throw AppException.Conflict("Phiên bản đã hết.");
        row.RevokedAt=DateTime.UtcNow;row.ScheduleEnabled=false;row.ConfigurationVersion++;row.UpdatedAt=row.RevokedAt;
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse.Ok("Revoked; not a physical stop."));
    }
}

[ApiController]
[Route("api/edge/water-bench")]
[Tags("Water bench edge configuration")]
public sealed class WaterBenchEdgeConfigurationController(AppDbContext db,IKioskProvisioningService kiosks):ControllerBase
{
    [AllowAnonymous]
    [HttpGet("configuration")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var session=await kiosks.GetSessionAsync(Request.Headers["X-Kiosk-Key"].FirstOrDefault()??"",ct);
        var id=session.Data?.KioskId??throw AppException.Unauthorized("Kiosk credential required.");
        var kiosk=await db.FarmKiosks.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Status!="Revoked",ct)
            ??throw AppException.Unauthorized("Kiosk revoked.");
        var row=await db.WaterBenchStations.AsNoTracking().SingleOrDefaultAsync(x=>x.KioskId==id&&x.RevokedAt==null,ct);
        Response.Headers["Cache-Control"]="no-store";
        return Ok(ApiResponse<object?>.Ok(row is null?null:WaterBenchConfiguration.View(row,await WaterBenchConfiguration.Ready(db,row,kiosk.FarmingAreaId,ct))));
    }
}
