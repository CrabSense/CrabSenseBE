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
[Route("api/areas/{areaId:guid}/water-analysis/color")]
[Authorize(Roles = AppRoles.FarmWrite)]
public sealed class WaterColorAnalysisController(AppDbContext db, IUnitOfWork uow, ICurrentUserService user, IMediaService media, WaterAiClient ai) : ControllerBase
{
    private readonly FarmAreaScope scope = new(uow,user);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Sources = ["RAS_RETURN","RETURN_TANK","BIO_TANK","SETTLING_TANK","INLET"];
    public record Standard(Guid MediaId, decimal Concentration, int ReactionSeconds, decimal SalinityPpt);
    public record CalibrationRequest(Guid CameraDeviceId,string Analyte,string KitName,string ReagentLot,
        ColorProtocol Protocol,Standard[] Standards,Standard Validation,decimal MaxValidationError,DateTimeOffset ValidUntil,string? MeasuredUnit=null,string? KitRevision=null);
    public record AnalyzeRequest(Guid CalibrationId,Guid MediaId,string SampleSource,string? SampleLocation,string? Notes,
        string ReagentLot,int ReactionSeconds,decimal SalinityPpt,Guid? BatchId=null);

    [HttpGet("calibrations")]
    public async Task<IActionResult> List(Guid areaId,[FromQuery] string? analyte,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var q=db.WaterColorCalibrations.AsNoTracking().Where(x=>x.FarmingAreaId==areaId);
        if(!string.IsNullOrWhiteSpace(analyte)) q=q.Where(x=>x.Analyte==analyte.Trim().ToUpperInvariant());
        return Ok(ApiResponse<object>.Ok(await q.OrderByDescending(x=>x.CreatedAt).Take(100).ToListAsync(ct)));
    }
    [HttpGet("calibrations/{id:guid}")]
    public async Task<IActionResult> Detail(Guid areaId,Guid id,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        return Ok(ApiResponse<WaterColorCalibration>.Ok(await Find(areaId,id,ct)));
    }
    [HttpPost("calibrations")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Create(Guid areaId,CalibrationRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        var analyte=request.Analyte?.Trim().ToUpperInvariant();
        var kit=request.KitName?.Trim();var lot=request.ReagentLot?.Trim();
        if(analyte is not ("NO2" or "NO3" or "NH3")||string.IsNullOrWhiteSpace(kit)||!string.Equals(kit,WaterBatchPlanning.Kit(analyte),StringComparison.OrdinalIgnoreCase)||
           string.IsNullOrWhiteSpace(lot)||lot.Length>120||request.Protocol is null||
           request.Standards is null||request.Standards.Length is <3 or >12||request.Validation is null||
           request.Standards.Any(x=>x is null)||request.MaxValidationError<=0||
           request.ValidUntil.UtcDateTime<=DateTime.UtcNow||request.ValidUntil.UtcDateTime>DateTime.UtcNow.AddDays(90))
            throw AppException.BadRequest("Thông tin hiệu chuẩn không hợp lệ; cần 3–12 mẫu, một đối chứng và hạn tối đa 90 ngày.");
        var unit=request.MeasuredUnit?.Trim() ?? (analyte=="NH3"?"":"mg/L "+analyte);
        if(analyte=="NH3" ? unit is not ("mg/L NH3" or "mg/L NH4" or "mg/L TAN-N") : unit!="mg/L "+analyte)
            throw AppException.BadRequest("Chọn đại lượng thực tế của thang màu; NH3/NH4/TAN-N không được dùng thay thế nhau.");
        var protocol=request.Protocol.Channel=="AUTO"?request.Protocol with {Channel="R"}:request.Protocol;
        WaterColorAnalyzer.ValidateProtocol(protocol);
        var revision=request.KitRevision?.Trim();
        if(string.IsNullOrWhiteSpace(revision)||revision.Length>120)
            throw AppException.BadRequest("Cần phiên bản bộ kit thực tế để tạo hiệu chuẩn AI.");
        await Camera(areaId,request.CameraDeviceId,ct);
        var photos=new List<WaterAiClient.Photo>();
        long total=0;
        foreach(var standard in request.Standards.Append(request.Validation))
        {
            CheckConditions(protocol,standard.ReactionSeconds,standard.SalinityPpt);
            var (bytes,_) = await ReadBytes(request.CameraDeviceId,standard.MediaId,ct);
            total+=bytes.Length;
            if(total>28L*1024*1024)throw AppException.BadRequest("Tổng ảnh hiệu chuẩn vượt 28 MiB.");
            photos.Add(new(standard.MediaId,bytes,standard.ReactionSeconds,standard.SalinityPpt,standard.Concentration));
        }
        var fitted=await ai.Fit(areaId,request.CameraDeviceId,analyte,WaterBatchPlanning.Kit(analyte),revision,unit,lot,
            protocol,photos.Take(photos.Count-1).ToArray(),photos[^1],request.MaxValidationError,request.ValidUntil.UtcDateTime,ct);
        protocol=protocol with {Channel=new[]{"R","G","B"}[fitted.Curve.GetProperty("channel").GetInt32()]};
        var profile=new WaterColorCalibration { FarmingAreaId=areaId,CameraDeviceId=request.CameraDeviceId,
            Analyte=analyte,Unit=unit,KitName=WaterBatchPlanning.Kit(analyte),ReagentLot=lot,CreatedBy=user.UserId,
            AiAlgorithm=fitted.Algorithm,AiCalibrationId=fitted.Id,AiCalibrationToken=fitted.Token,KitRevision=revision,
            ValidUntil=fitted.ValidUntil,ProtocolJson=JsonSerializer.Serialize(protocol,Json),
            CurveJson=fitted.Curve.GetRawText(),ValidationJson=JsonSerializer.Serialize(new {
                sample=request.Validation,checks=fitted.Validation,absoluteError=fitted.Validation[0].GetProperty("absolute_error").GetDecimal(),
                request.MaxValidationError,standards=request.Standards,algorithm=fitted.Algorithm },Json) };
        db.WaterColorCalibrations.Add(profile);
        Audit("CreateWaterColorCalibration",profile.Id,analyte);
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<WaterColorCalibration>.Ok(profile));
    }

    [HttpPost("calibrations/{id:guid}/revoke")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> Revoke(Guid areaId,Guid id,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        var profile=await Find(areaId,id,ct);
        if(profile.RevokedAt is null){profile.RevokedAt=DateTime.UtcNow;Audit("RevokeWaterColorCalibration",id,profile.Analyte);await db.SaveChangesAsync(ct);}
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(Guid areaId,AnalyzeRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var profile=await Find(areaId,request.CalibrationId,ct);
        if(profile.RevokedAt.HasValue||profile.ValidUntil<=DateTime.UtcNow)
            throw AppException.Conflict("Hiệu chuẩn đã hết hạn hoặc bị thu hồi.");
        if(!string.Equals(request.ReagentLot?.Trim(),profile.ReagentLot,StringComparison.Ordinal))
            throw AppException.BadRequest("Lô thuốc thử không trùng hiệu chuẩn.");
        var source=request.SampleSource?.Trim().ToUpperInvariant();
        if(!Sources.Contains(source)||request.SampleLocation?.Length>128||request.Notes?.Length>500)
            throw AppException.BadRequest("Thông tin nguồn mẫu không hợp lệ.");
        var protocol=JsonSerializer.Deserialize<ColorProtocol>(profile.ProtocolJson,Json)!;
        CheckConditions(protocol,request.ReactionSeconds,request.SalinityPpt);
        var camera=await Camera(areaId,profile.CameraDeviceId,ct);
        var (bytes,asset)=await ReadBytes(profile.CameraDeviceId,request.MediaId,ct);
        var existing=await db.WaterAnalysisRuns.AsNoTracking().FirstOrDefaultAsync(x=>x.SampleMediaId==request.MediaId,ct);
        if(existing is not null)
        {
            if(existing.FarmingAreaId!=areaId||existing.CalibrationId!=profile.Id||existing.BatchId!=request.BatchId) throw AppException.Conflict("Ảnh đã được sử dụng trong phiên khác.");
            return Ok(ApiResponse<object>.Ok(View(existing,profile)));
        }
        if(asset.CreatedAt < profile.CreatedAt || asset.CreatedAt > DateTime.UtcNow.AddMinutes(1))
            throw AppException.BadRequest("Ảnh mẫu phải được tải lên sau khi hiệu chuẩn được tạo.");
        if(request.BatchId is Guid requestedBatch)
        {
            var ready=await db.WaterAssayChannels.AsNoTracking().FirstOrDefaultAsync(x=>x.BatchId==requestedBatch&&x.CalibrationId==profile.Id,ct);
            var active=await db.WaterAnalysisBatches.AsNoTracking().AnyAsync(x=>x.Id==requestedBatch&&x.FarmingAreaId==areaId&&x.Status=="waiting_samples"&&x.SampleSource==source,ct);
            if(!active||ready is null||WaterAssayFlow.Current(ready)!="analyze"||ready.CapturedMediaId!=asset.Id)
                throw AppException.Conflict("Kênh chưa sẵn sàng phân tích ảnh đã xác nhận.");
        }
        decimal value,validationError;
        object reading;
        var algorithm=profile.AiAlgorithm??WaterColorAnalyzer.Version;
        if(profile.AiAlgorithm is not null)
        {
            if(algorithm!=WaterAiClient.Algorithm||string.IsNullOrWhiteSpace(profile.AiCalibrationToken)||string.IsNullOrWhiteSpace(profile.AiCalibrationId))
                throw AppException.Conflict("Hồ sơ hiệu chuẩn AI không hợp lệ; hiệu chuẩn lại.");
            var estimate=await ai.Analyze(areaId,profile.CameraDeviceId,profile.Analyte,profile.Unit,profile.ReagentLot,
                profile.AiCalibrationToken,profile.AiCalibrationId,new(asset.Id,bytes,request.ReactionSeconds,request.SalinityPpt),ct);
            value=estimate.Value;reading=estimate.Evidence;validationError=estimate.HoldoutError;
        }
        else
        {
            // Legacy local curves remain version-pinned; never fall back from remote inference.
            var points=JsonSerializer.Deserialize<ColorPoint[]>(profile.CurveJson,Json)!;
            using var validation=JsonDocument.Parse(profile.ValidationJson);
            var local=WaterColorAnalyzer.Read(bytes,protocol);
            var checkHash=validation.RootElement.GetProperty("reading").GetProperty("sha256").GetString();
            if(points.Any(x=>x.Reading.Sha256==local.Sha256)||local.Sha256==checkHash)
                throw AppException.BadRequest("Không dùng lại ảnh chuẩn/đối chứng để đo mẫu.");
            value=WaterColorAnalyzer.Predict(points,local);reading=local;
            validationError=validation.RootElement.GetProperty("absoluteError").GetDecimal();
        }
        var now=DateTime.UtcNow;
        var actor=await scope.CurrentUserAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        WaterAnalysisBatch? batch=null;
        WaterAssayChannel? channel=null;
        if(request.BatchId is Guid batchId)
        {
            var locked=await db.WaterAnalysisBatches.Where(x=>x.Id==batchId&&x.FarmingAreaId==areaId&&x.Status=="waiting_samples")
                .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UpdatedAt,now),ct);
            if(locked!=1)throw AppException.Conflict("Phiên đo không tồn tại hoặc đã kết thúc.");
            batch=await db.WaterAnalysisBatches.FirstAsync(x=>x.Id==batchId,ct);
            var targets=JsonSerializer.Deserialize<WaterBatchPlanning.Target[]>(batch.TargetsJson,WaterBatchPlanning.Json)!;
            if(!targets.Any(x=>x.CalibrationId==profile.Id&&x.Analyte==profile.Analyte)||batch.SampleSource!=source||asset.CreatedAt<batch.CreatedAt)
                throw AppException.BadRequest("Ảnh, nguồn mẫu hoặc hiệu chuẩn không thuộc phiên đo.");
            if(await db.WaterAnalysisRuns.AnyAsync(x=>x.BatchId==batchId&&x.Analyte==profile.Analyte,ct))
                throw AppException.Conflict("Chỉ tiêu đã có kết quả trong phiên này.");
            channel=await db.WaterAssayChannels.FirstOrDefaultAsync(x=>x.BatchId==batchId&&x.CalibrationId==profile.Id,ct)
                ??throw AppException.Conflict("Phiên chưa có kênh quy trình.");
            WaterAssayFlow.ResultSaved(channel,asset.Id,now,user.UserId);
        }
        if(!await db.WaterColorCalibrations.AnyAsync(x=>x.Id==profile.Id&&x.RevokedAt==null&&x.ValidUntil>now,ct))
            throw AppException.Conflict("Hiệu chuẩn không còn hợp lệ.");
        var run=new WaterAnalysisRun { FarmingAreaId=areaId, Analyte=profile.Analyte, Status="completed",
            CurrentStep=7, StartedAt=now, LastStepAt=now,CompletedAt=now,ResultRecordedAt=now,
            Source=profile.AiAlgorithm is null?"calibrated-color":"water-ai-python",ExecutionMode="manual-color",CalibrationId=profile.Id,SampleMediaId=asset.Id,
            BatchId=request.BatchId,MeasuredValue=value,MeasuredUnit=profile.Unit,Nh3=profile.Unit=="mg/L NH3"?value:null,
            No2=profile.Analyte=="NO2"?value:null,No3=profile.Analyte=="NO3"?value:null,
            TestCode="COLOR-"+Guid.NewGuid().ToString("N")[..20],SampleSource=source,SampleLocation=request.SampleLocation,
            Notes=request.Notes,PerformedBy=actor?.FullName is { Length: >0 } name ? name[..Math.Min(128,name.Length)] : user.UserId.ToString(),
            CameraId=camera.DeviceCode[..Math.Min(64,camera.DeviceCode.Length)],
            ColorEvidenceJson=JsonSerializer.Serialize(new { algorithm,aiCalibrationId=profile.AiCalibrationId,kitRevision=profile.KitRevision,reading,request.ReactionSeconds,request.SalinityPpt,
                request.ReagentLot,unit=profile.Unit,value,mediaUploadedAt=asset.CreatedAt,validationError,
                timingSource="operator-attested",assayProfileId=channel?.ProfileId,assayChannelId=channel?.Id,cleaning="not-performed-by-api" },Json),
            StepLogJson=JsonSerializer.Serialize(new[]{
                new { At=now,Step=5,Event="IMAGE_PROVIDED",Label="Ảnh mẫu đã lưu" },
                new { At=now,Step=6,Event="COLOR_ESTIMATED",Label="Nội suy từ đường hiệu chuẩn" },
                new { At=now,Step=7,Event="ANALYSIS_RESULT_SAVED",Label="Đã lưu kết quả" }}) };
        db.WaterAnalysisRuns.Add(run);
        Audit("AnalyzeWaterColor",run.Id,profile.Analyte);
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}catch(DbUpdateException){throw AppException.Conflict("Không thể lưu; kiểm tra ảnh đã được xử lý.");}
        return Ok(ApiResponse<object>.Ok(View(run,profile)));
    }

    private static object View(WaterAnalysisRun run,WaterColorCalibration profile)=>new {
        run.Id,run.TestCode,run.Status,run.Analyte,result=run.MeasuredValue??run.No2??run.No3??run.Nh3,profile.Unit,run.CalibrationId,run.BatchId,
        run.SampleMediaId,run.ColorEvidenceJson,run.CompletedAt,method=profile.AiAlgorithm??WaterColorAnalyzer.Version };
    private async Task<WaterColorCalibration> Find(Guid areaId,Guid id,CancellationToken ct)=>
        await db.WaterColorCalibrations.FirstOrDefaultAsync(x=>x.Id==id&&x.FarmingAreaId==areaId,ct)
        ??throw AppException.NotFound("WaterColorCalibration");
    private async Task<Device> Camera(Guid areaId,Guid id,CancellationToken ct)
    {
        var d=await db.Devices.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id&&x.FarmingAreaId==areaId,ct);
        if(d is null||(!(d.DeviceType??"").Contains("cam",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrWhiteSpace(d.SnapshotUrl)&&string.IsNullOrWhiteSpace(d.StreamUrl)))
            throw AppException.BadRequest("Camera buồng đo phải được đăng ký đúng khu.");
        return d;
    }
    private async Task<(byte[] bytes,MediaAsset asset)> ReadBytes(Guid cameraId,Guid mediaId,CancellationToken ct)
    {
        var asset=await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==mediaId,ct)??throw AppException.NotFound("Media");
        if(asset.DeviceId!=cameraId||asset.Category!="image"||asset.BoxId!=null||asset.CrabId!=null||asset.SizeBytes<=0||asset.SizeBytes>5*1024*1024||
           asset.RelatedEntityType!="device"||asset.RelatedEntityId!=cameraId||asset.IsShared)
            throw AppException.BadRequest("Cần ảnh riêng của camera buồng đo (không gắn hộp/cua), tối đa 5 MB.");
        var content=await media.OpenContentAsync(mediaId,ct);
        await using var input=content.Content;
        using var buffer=new MemoryStream();
        var chunk=new byte[65536];int n;
        while((n=await input.ReadAsync(chunk.AsMemory(),ct))>0)
        {if(buffer.Length+n>5*1024*1024)throw AppException.BadRequest("Ảnh quá lớn.");await buffer.WriteAsync(chunk.AsMemory(0,n),ct);}
        return (buffer.ToArray(),asset);
    }
    private static void CheckConditions(ColorProtocol p,int seconds,decimal salinity)
    {
        if(seconds<0||Math.Abs((long)seconds-p.ReactionSeconds)>p.TimingToleranceSeconds||salinity<p.MinSalinityPpt||salinity>p.MaxSalinityPpt)
            throw AppException.BadRequest("Thời gian phản ứng hoặc độ mặn ngoài phạm vi hiệu chuẩn.");
    }
    private void Audit(string action,Guid id,string details)=>db.OperationLogs.Add(new OperationLog{
        UserId=user.UserId,Action=action,EntityType="WaterColorAnalysis",EntityId=id,Details=details,IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString() });
}
