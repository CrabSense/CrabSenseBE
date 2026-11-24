using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.DTOs.Media;
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
public sealed class WaterAnalysisBatchController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user,IMediaService media):ControllerBase
{
    private readonly FarmAreaScope scope=new(uow,user);
    public record StartRequest(WaterBatchPlanning.Target[] Targets,string SampleSource);
    public record ScheduleRequest(WaterBatchPlanning.Target[] Targets,string SampleSource,int IntervalMinutes,DateTimeOffset NextRunAt,bool Enabled);

    [HttpGet("assay-profiles")]
    public async Task<IActionResult> AssayProfiles(Guid areaId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        return Ok(ApiResponse<object>.Ok(WaterAssayCatalog.All()));
    }
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
        await WaterBatchPlanning.AddChannels(db,batch,targets,now,ct);
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
        var channels=await db.WaterAssayChannels.AsNoTracking().Where(x=>x.BatchId==id).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{batch,tests=targets.Select(t=>{
            var channel=channels.SingleOrDefault(x=>x.Analyte==t.Analyte);
            return new{t.Analyte,t.CalibrationId,kit=WaterBatchPlanning.Kit(t.Analyte),
                status=channel?.State??batch.Status,channel,step=channel is null?null:WaterAssayFlow.Current(channel),
                result=runs.FirstOrDefault(x=>x.Analyte==t.Analyte)};
        }),dispatch="not_integrated",execution="operator_assisted"}));
    }
    private static string SampleMarker(Guid batchId,string analyte) =>
        $"water-batch:{batchId:D}:{analyte}";

    [HttpPost("batches/{id:guid}/channels/{analyte}/sample")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadSample(Guid areaId,Guid id,string analyte,IFormFile file,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        if(file is null||file.Length<=0||file.Length>5*1024*1024)
            throw AppException.BadRequest("Ảnh mẫu phải có kích thước tối đa 5 MiB.");
        var batch=await db.WaterAnalysisBatches.AsNoTracking().SingleOrDefaultAsync(
            x=>x.Id==id&&x.FarmingAreaId==areaId&&x.Status=="waiting_samples",ct)
            ??throw AppException.Conflict("Phiên không tồn tại hoặc đã kết thúc.");
        var chemical=analyte.Trim().ToUpperInvariant();
        var channel=await db.WaterAssayChannels.AsNoTracking().SingleOrDefaultAsync(
            x=>x.BatchId==id&&x.Analyte==chemical,ct)
            ??throw AppException.NotFound("Water assay channel");
        var now=DateTime.UtcNow;
        if(WaterAssayFlow.Current(channel)!="capture"||channel.ReactionReadyAt is not DateTime ready||now<ready)
            throw AppException.Conflict("Kênh chưa đến bước chụp ảnh.");
        var profile=await db.WaterColorCalibrations.AsNoTracking().SingleOrDefaultAsync(
            x=>x.Id==channel.CalibrationId&&x.FarmingAreaId==areaId&&x.Analyte==chemical&&
                x.RevokedAt==null&&x.ValidUntil>now&&(x.QualityCheckDueAt==null||x.QualityCheckDueAt>now),ct)
            ??throw AppException.Conflict("Hiệu chuẩn không còn hiệu lực.");
        var protocol=JsonSerializer.Deserialize<ColorProtocol>(channel.ProtocolJson,WaterBatchPlanning.Json)
            ??throw AppException.Conflict("Thiếu cấu hình thời gian đọc màu.");
        if(now>ready.AddSeconds(protocol.TimingToleranceSeconds))
            throw AppException.Conflict("Đã quá cửa sổ đọc màu.");
        if(!await db.Devices.AnyAsync(x=>x.Id==profile.CameraDeviceId&&x.FarmingAreaId==areaId,ct))
            throw AppException.BadRequest("Camera không thuộc khu phân tích.");
        var area=await db.FarmingAreas.AsNoTracking().SingleAsync(x=>x.Id==areaId,ct);
        var folder=MediaStorageLayout.WaterRun(MediaFolderPath.Leaf(area.Id,area.Code),
            id,id,chemical,batch.CreatedAt);
        var meta=new MediaUploadMeta("image",null,null,profile.CameraDeviceId,"device",
            profile.CameraDeviceId,SampleMarker(id,chemical),false){ServerFolder=folder};
        await using var stream=file.OpenReadStream();
        return Ok(await media.UploadAsync(stream,file.FileName,file.ContentType,meta,user.UserId,ct));
    }

    public record StepRequest(string Step,string Action,Guid? MediaId=null,string? Reason=null,bool KitInstructionsConfirmed=false);

    [HttpPost("batches/{id:guid}/channels/{analyte}/steps")]
    public async Task<IActionResult> Step(Guid areaId,Guid id,string analyte,StepRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var now=DateTime.UtcNow;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var locked=await db.WaterAnalysisBatches.Where(x=>x.Id==id&&x.FarmingAreaId==areaId&&x.Status=="waiting_samples")
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UpdatedAt,now),ct);
        if(locked!=1)throw AppException.Conflict("Phiên không tồn tại hoặc đã kết thúc.");
        var batch=await db.WaterAnalysisBatches.FirstAsync(x=>x.Id==id,ct);
        var channels=await db.WaterAssayChannels.Where(x=>x.BatchId==id).ToListAsync(ct);
        var channel=channels.SingleOrDefault(x=>x.Analyte==analyte.Trim().ToUpperInvariant())
            ??throw AppException.NotFound("Water assay channel");
        if(request.Action=="fail")
        {
            if(request.MediaId.HasValue)throw AppException.BadRequest("Báo lỗi không nhận ảnh.");
            WaterAssayFlow.Fail(channel,request.Step,request.Reason??"",now,user.UserId);
        }
        else if(request.Action=="confirm")
        {
            if(request.Step=="rinse"&&!request.KitInstructionsConfirmed)
                throw AppException.BadRequest("Xác nhận đã đối chiếu đúng phiên bản kit, hướng dẫn, lô thuốc và điều kiện mẫu trước khi bắt đầu.");
            if(request.Step!="cleanup"&&!await db.WaterColorCalibrations.AnyAsync(x=>x.Id==channel.CalibrationId&&x.RevokedAt==null&&x.ValidUntil>now&&(x.QualityCheckDueAt==null||x.QualityCheckDueAt>now),ct))
                throw AppException.Conflict("Hiệu chuẩn hết hiệu lực; báo lỗi và làm sạch.");
            if(request.Step=="capture"&&request.MediaId is Guid mediaId)
            {
                await media.GetByIdAsync(mediaId,ct);
                var profile=await db.WaterColorCalibrations.AsNoTracking().FirstAsync(x=>x.Id==channel.CalibrationId,ct);
                var asset=await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==mediaId,ct)??throw AppException.NotFound("Media");
                if(asset.DeviceId!=profile.CameraDeviceId||asset.Category!="image"||asset.BoxId!=null||asset.CrabId!=null||
                    asset.RelatedEntityType!="device"||asset.RelatedEntityId!=profile.CameraDeviceId||asset.IsShared||
                    (asset.Notes!=null&&asset.Notes.StartsWith("water-batch:",StringComparison.OrdinalIgnoreCase)&&
                        asset.Notes!=SampleMarker(id,channel.Analyte))||
                    asset.SizeBytes<=0||asset.SizeBytes>5*1024*1024||channel.ReactionReadyAt is not DateTime ready||
                    asset.CreatedAt<ready||asset.CreatedAt>now.AddSeconds(1)||
                    await db.WaterAssayChannels.AnyAsync(x=>x.CapturedMediaId==mediaId&&x.Id!=channel.Id,ct))
                    throw AppException.BadRequest("Ảnh phải riêng tư, đúng camera, tải sau khi đủ thời gian và chưa dùng cho kênh khác.");
            }
            WaterAssayFlow.Confirm(channel,request.Step,now,user.UserId,request.MediaId);
        }
        else throw AppException.BadRequest("Action phải là confirm hoặc fail.");
        var status=WaterAssayFlow.BatchStatus(channels);
        if(status!="waiting_samples"){batch.Status=status;batch.CompletedAt=now;}
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{channel,step=WaterAssayFlow.Current(channel),batch.Status,execution="operator_assisted"}));
    }
    [HttpPost("batches/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid areaId,Guid id,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var now=DateTime.UtcNow;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var locked=await db.WaterAnalysisBatches.Where(x=>x.Id==id&&x.FarmingAreaId==areaId&&x.Status=="waiting_samples")
            .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UpdatedAt,now),ct);
        if(locked!=1)throw AppException.Conflict("Phiên không tồn tại hoặc đã kết thúc.");
        var channels=await db.WaterAssayChannels.Where(x=>x.BatchId==id).ToListAsync(ct);
        if(channels.Any(x=>!WaterAssayFlow.Terminal(x)&&x.State!="waiting"))
            throw AppException.Conflict("Kênh đã bắt đầu; báo lỗi và xác nhận thu gom/rửa trước khi kết thúc phiên.");
        foreach(var channel in channels.Where(x=>x.State=="waiting")){channel.State="cancelled";channel.UpdatedAt=now;}
        var batch=await db.WaterAnalysisBatches.FirstAsync(x=>x.Id==id,ct);
        batch.Status=channels.Any(x=>x.State=="completed")?"partial":"cancelled";batch.CompletedAt=now;
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
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
