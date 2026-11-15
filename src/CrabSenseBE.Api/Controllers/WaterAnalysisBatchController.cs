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
[Route("api/areas/{areaId:guid}/water-analysis")]
[Authorize(Roles=AppRoles.FarmWrite)]
public sealed class WaterAnalysisBatchController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    private readonly FarmAreaScope scope=new(uow,user);
    public record StartRequest(WaterBatchPlanning.Target[] Targets,string SampleSource);
    public record ScheduleRequest(WaterBatchPlanning.Target[] Targets,string SampleSource,int IntervalMinutes,DateTimeOffset NextRunAt,bool Enabled);

    [HttpPost("batches")]
    public async Task<IActionResult> Start(Guid areaId,StartRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var targets=WaterBatchPlanning.Normalize(request.Targets,false);
        var source=WaterBatchPlanning.Source(request.SampleSource);
        var now=DateTime.UtcNow;
        if(!await WaterBatchPlanning.ProfilesReady(db,areaId,targets,now,ct)) throw AppException.Conflict("Hiệu chuẩn chưa sẵn sàng, sai bộ kit hoặc hết hạn.");
        if(await db.WaterAnalysisRuns.AnyAsync(x=>x.FarmingAreaId==areaId&&(x.Status=="running"||x.Status=="stopping"),ct))
            throw AppException.Conflict("Trạm đang chạy một quy trình phần cứng.");
        var batch=WaterBatchPlanning.New(areaId,"manual",targets,source,now,user.UserId);
        db.WaterAnalysisBatches.Add(batch);
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Khu đang có phiên đo chờ mẫu.");}
        return Ok(ApiResponse<object>.Ok(new{batch.Id,batch.Status,batch.Trigger,targets,dispatch="not_integrated"}));
    }
    [HttpGet("batches")]
    public async Task<IActionResult> List(Guid areaId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        return Ok(ApiResponse<object>.Ok(await db.WaterAnalysisBatches.AsNoTracking().Where(x=>x.FarmingAreaId==areaId)
            .OrderByDescending(x=>x.CreatedAt).Take(50).ToListAsync(ct)));
    }
    [HttpGet("batches/{id:guid}")]
    public async Task<IActionResult> Detail(Guid areaId,Guid id,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var batch=await db.WaterAnalysisBatches.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id&&x.FarmingAreaId==areaId,ct)
            ??throw AppException.NotFound("Water analysis batch");
        var targets=JsonSerializer.Deserialize<WaterBatchPlanning.Target[]>(batch.TargetsJson,WaterBatchPlanning.Json)!;
        var runs=await db.WaterAnalysisRuns.AsNoTracking().Where(x=>x.BatchId==id).Select(x=>new{x.Id,x.Analyte,x.MeasuredValue,x.MeasuredUnit,x.Status,x.SampleMediaId,x.CalibrationId}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{batch,tests=targets.Select(t=>new{t.Analyte,t.CalibrationId,kit=WaterBatchPlanning.Kit(t.Analyte),
            status=runs.Any(x=>x.Analyte==t.Analyte)?"completed":batch.Status=="cancelled"?"cancelled":"waiting_sample",
            result=runs.FirstOrDefault(x=>x.Analyte==t.Analyte)}),dispatch="not_integrated"}));
    }
    [HttpPost("batches/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid areaId,Guid id,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var now=DateTime.UtcNow;
        var changed=await db.WaterAnalysisBatches.Where(x=>x.Id==id&&x.FarmingAreaId==areaId&&x.Status=="waiting_samples")
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.Status,"cancelled").SetProperty(x=>x.CompletedAt,now),ct);
        if(changed!=1)throw AppException.Conflict("Phiên không tồn tại hoặc đã kết thúc.");
        return Ok(ApiResponse.Ok());
    }
    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule(Guid areaId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        return Ok(ApiResponse<object?>.Ok(await db.WaterAnalysisSchedules.AsNoTracking().FirstOrDefaultAsync(x=>x.FarmingAreaId==areaId,ct)));
    }
    [HttpPut("schedule")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Configure(Guid areaId,ScheduleRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        var targets=WaterBatchPlanning.Normalize(request.Targets,true);
        var source=WaterBatchPlanning.Source(request.SampleSource);
        var now=DateTime.UtcNow;
        if(request.IntervalMinutes is <15 or >10080 || request.NextRunAt.UtcDateTime<=now || request.NextRunAt.UtcDateTime>now.AddDays(30))
            throw AppException.BadRequest("Chọn thời điểm tương lai (tối đa 30 ngày) và chu kỳ 15–10080 phút.");
        if(request.Enabled&&!await WaterBatchPlanning.ProfilesReady(db,areaId,targets,now,ct))
            throw AppException.Conflict("Cần đủ 3 hiệu chuẩn đúng kit trước khi bật lịch.");
        var row=await db.WaterAnalysisSchedules.FirstOrDefaultAsync(x=>x.FarmingAreaId==areaId,ct);
        if(row is null){row=new WaterAnalysisSchedule{FarmingAreaId=areaId};db.WaterAnalysisSchedules.Add(row);}
        row.TargetsJson=JsonSerializer.Serialize(targets,WaterBatchPlanning.Json);
        row.SampleSource=source;row.IntervalMinutes=request.IntervalMinutes;row.NextRunAt=request.NextRunAt.UtcDateTime;row.Enabled=request.Enabled;
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Cấu hình lịch vừa thay đổi; tải lại.");}
        return Ok(ApiResponse<WaterAnalysisSchedule>.Ok(row));
    }
}
