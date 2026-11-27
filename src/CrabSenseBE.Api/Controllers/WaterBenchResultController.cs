using System.Security.Cryptography;
using System.Text;
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
[Route("api/areas/{areaId:guid}/water-bench/batches/{stationId:guid}/{bootId}/{batchId}/results")]
[Authorize(Roles=AppRoles.FarmWrite)]
public sealed class WaterBenchResultController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user,IMediaService media,WaterAiClient ai):ControllerBase
{
    private readonly FarmAreaScope scope=new(uow,user);
    public record AssessRequest(Guid RequestId,string Action,string Reason,Guid? CalibrationId=null,string? ReagentLot=null,
        int? ReactionSeconds=null,decimal? SalinityPpt=null,string? SampleSource=null,bool OperatorConditionsConfirmed=false);
    public record BatchAssessItem(string Analyte,AssessRequest Assessment);
    public record BatchAssessRequest(IReadOnlyList<BatchAssessItem> Samples);
    private IQueryable<WaterBenchEvidence> Scoped(Guid areaId,Guid stationId,string bootId,string batchId)=>
        from e in db.WaterBenchEvidence.AsNoTracking()
        join s in db.WaterBenchStations on e.StationId equals s.Id
        join k in db.FarmKiosks on s.KioskId equals k.Id
        where k.FarmingAreaId==areaId&&s.Id==stationId&&e.BootId==bootId&&e.FirmwareBatchId==batchId
        select e;
    private async Task<WaterBenchBatchTimeline> Timeline(IQueryable<WaterBenchEvidence> q,string boot,string batch,CancellationToken ct)
    {
        if(!WaterBenchBatchTimeline.Hex16(boot)||!WaterBenchBatchTimeline.Hex16(batch))
            throw AppException.BadRequest("Invalid batch identity.");
        var rows=await q.Where(x=>x.Kind=="batch_start"||x.Kind=="session"||x.Kind=="batch_status"||
            x.Kind=="image_request"||x.Kind=="operator_event").Select(x=>x.PayloadJson)
            .Take(WaterBenchBatchTimeline.MaxEvents+1).ToListAsync(ct);
        if(rows.Count==0)throw AppException.NotFound("Water bench batch");
        return WaterBenchBatchTimeline.Replay(rows);
    }
    private static object View(WaterBenchResultAssessment a)=>new {
        a.Id,a.SessionEvidenceId,a.Status,a.Reason,a.MediaId,a.CalibrationId,
        estimatedValue=a.CandidateValue,a.Unit,a.HoldoutError,confidence=(decimal?)null,
        a.AssessedBy,a.CreatedAt,a.EvidenceJson,chemicalResultValidated=false,productionPublished=false,
        ammoniaConversionApplied=false
    };
    [HttpGet]
    public async Task<IActionResult> Get(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var q=Scoped(areaId,stationId,bootId,batchId);
        var timeline=await Timeline(q,bootId,batchId,ct);
        var sessions=await q.Where(x=>x.Kind=="session").ToListAsync(ct);
        var images=await q.Where(x=>x.Kind=="jpeg").Select(x=>new{x.FirmwareSessionId,x.Analyte,x.CaptureId,x.ConnectionId}).ToListAsync(ct);
        var ids=sessions.Select(x=>x.Id).ToArray();
        var results=await db.WaterBenchResultAssessments.AsNoTracking().Where(x=>ids.Contains(x.SessionEvidenceId)).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{timeline.Status,channels=timeline.Channels.Select(c=>{
            var session=sessions.SingleOrDefault(s=>s.Analyte==c.Analyte&&s.FirmwareSessionId==c.SessionId);
            var a=results.SingleOrDefault(x=>x.SessionEvidenceId==session?.Id);
            var linked=images.Count(i=>i.Analyte==c.Analyte&&i.FirmwareSessionId==c.SessionId&&i.CaptureId==c.CaptureId&&i.ConnectionId==timeline.ConnectionId)==1;
            string status=a?.Status??(!WaterBenchResultPolicy.ReadyForEstimate(timeline,c)?
                (timeline.Status is "fault_reported" or "interrupted_observed"?"incomplete_evidence":"awaiting_firmware_completion"):
                !linked?"awaiting_media":"awaiting_assessment");
            return new{c.Analyte,c.SessionId,c.CaptureId,status,assessment=a is null?null:View(a),productionPublished=false};
        }),productionPublished=false}));
    }
    // Metadata-only preflight. Does not read image bytes, call AI, or create a result.
    [HttpGet("ai-readiness")]
    public async Task<IActionResult> AiReadiness(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        Response.Headers["Cache-Control"]="no-store";
        var q=Scoped(areaId,stationId,bootId,batchId);
        var timeline=await Timeline(q,bootId,batchId,ct);
        var channels=new List<object>();
        foreach(var c in timeline.Channels)
        {
            if(!WaterBenchResultPolicy.ReadyForEstimate(timeline,c))
            {
                channels.Add(new{c.Analyte,c.SessionId,c.CaptureId,status="awaiting_terminal_evidence"});continue;
            }
            var session=await q.SingleOrDefaultAsync(x=>x.Kind=="session"&&x.Analyte==c.Analyte&&
                x.FirmwareSessionId==c.SessionId&&x.ConnectionId==timeline.ConnectionId,ct);
            if(session?.KioskReceivedAt is not DateTime observed)
            {channels.Add(new{c.Analyte,status="missing_session"});continue;}
            if(await db.WaterBenchResultAssessments.AsNoTracking().AnyAsync(x=>x.SessionEvidenceId==session.Id,ct))
            {channels.Add(new{c.Analyte,status="already_assessed"});continue;}
            var receipts=await q.Where(x=>x.Kind=="jpeg"&&x.ConnectionId==timeline.ConnectionId&&
                x.FirmwareSessionId==c.SessionId&&x.Analyte==c.Analyte&&x.CaptureId==c.CaptureId).Take(2).ToListAsync(ct);
            if(receipts.Count!=1)
            {channels.Add(new{c.Analyte,status=receipts.Count==0?"awaiting_media":"ambiguous_media"});continue;}
            WaterBenchResultPolicy.CaptureReceipt receipt;
            try{receipt=WaterBenchResultPolicy.VerifiedReceipt(receipts[0]);}
            catch(AppException){channels.Add(new{c.Analyte,status="invalid_media_receipt"});continue;}
            var profile=await db.WaterColorCalibrations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receipt.CalibrationId&&
                x.FarmingAreaId==areaId,ct);
            if(profile==null||!WaterBenchResultPolicy.ReceiptMatches(receipt,profile.Id,profile.CameraDeviceId)||
                !await db.Devices.AnyAsync(x=>x.Id==profile.CameraDeviceId&&x.FarmingAreaId==areaId,ct))
            {channels.Add(new{c.Analyte,status="calibration_or_camera_mismatch"});continue;}
            ColorProtocol protocol;
            try{protocol=WaterBenchResultPolicy.Profile(profile,c.Analyte,observed,DateTime.UtcNow);}
            catch(AppException){channels.Add(new{c.Analyte,status="calibration_unavailable"});continue;}
            var asset=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receipt.MediaId,ct);
            if(asset==null||!WaterBenchResultPolicy.AssetMatches(asset,profile.CameraDeviceId,stationId,bootId,batchId,c.Analyte,c.CaptureId!.Value))
            {channels.Add(new{c.Analyte,status="invalid_private_media"});continue;}
            if(await Used(areaId,receipt.MediaId,receipt.Sha256,ct))
            {channels.Add(new{c.Analyte,status="media_already_used"});continue;}
            channels.Add(new{c.Analyte,c.SessionId,c.CaptureId,status="operator_confirmation_required",
                calibrationId=profile.Id,mediaId=receipt.MediaId,cameraId=profile.CameraDeviceId,profile.Unit,profile.ReagentLot,
                algorithm=profile.AiAlgorithm,profile.KitRevision,profile.ValidUntil,receipt.ConfigurationVersion,
                reactionSeconds=protocol.ReactionSeconds,timingToleranceSeconds=protocol.TimingToleranceSeconds,
                minSalinityPpt=protocol.MinSalinityPpt,maxSalinityPpt=protocol.MaxSalinityPpt});
        }
        return Ok(ApiResponse<object>.Ok(new{timeline.Status,channels,metadataOnly=true,aiCalled=false,
            actualSampleConditionsVerified=false,chemicalResultValidated=false,productionPublished=false}));
    }
    // One HTTP operation for 1–3 independent assessments; never share an EF context across concurrent tasks.
    [HttpPost("batch-assessment")]
    [Authorize(Roles=AppRoles.FarmManage)]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> AssessBatch(Guid areaId,Guid stationId,string bootId,string batchId,BatchAssessRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        if(request.Samples is null||request.Samples.Count is <1 or >3||
            request.Samples.Any(s=>s is null||s.Assessment is null||s.Assessment.Action!="estimate"||
                !s.Assessment.OperatorConditionsConfirmed||s.Assessment.CalibrationId is null||
                s.Assessment.CalibrationId==Guid.Empty||s.Assessment.ReactionSeconds is null||
                s.Assessment.SalinityPpt is null||string.IsNullOrWhiteSpace(s.Assessment.ReagentLot)||
                s.Assessment.ReagentLot.Length>128||string.IsNullOrWhiteSpace(s.Assessment.Reason)||s.Assessment.Reason.Length>500))
            throw AppException.BadRequest("Mỗi mẫu cần xác nhận điều kiện và hiệu chuẩn riêng.");
        var timeline=await Timeline(Scoped(areaId,stationId,bootId,batchId),bootId,batchId,ct);
        WaterBenchResultPolicy.ValidateBatchSelection(timeline,request.Samples.Select(s=>s.Analyte).ToArray(),
            request.Samples.Select(s=>s.Assessment.RequestId).ToArray());
        // Validate source syntax for the whole request before doing any external IO.
        foreach(var item in request.Samples)_=WaterBatchPlanning.Source(item.Assessment.SampleSource);
        var outcomes=new Dictionary<string,object>(StringComparer.Ordinal);
        var prepared=new List<(BatchAssessItem Item,PreparedAssessment Prepared)>();
        int succeeded=0;
        void Reject(BatchAssessItem item,int status,string message)=>outcomes[item.Analyte]=new
        {
            item.Analyte,requestId=item.Assessment.RequestId,status="assessment_rejected",httpStatus=status,
            retryable=status is 502 or 503,message
        };
        void Accept(BatchAssessItem item,WaterBenchResultAssessment row)
        {
            succeeded++;
            outcomes[item.Analyte]=new{item.Analyte,requestId=item.Assessment.RequestId,status=row.Status,
                assessment=ApiResponse<object>.Ok(View(row))};
        }
        // Database and private-storage reads are sequential on this request's scoped EF context.
        foreach(var item in request.Samples)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var p=await PrepareAssessment(areaId,stationId,bootId,batchId,item.Analyte,item.Assessment,ct);
                if(p.Existing)Accept(item,p.Row);
                else prepared.Add((item,p));
            }
            catch(AppException ex) when(ex.StatusCode is 400 or 404 or 409 or 502 or 503)
            {Reject(item,ex.StatusCode,ex.Message);}
            finally{db.ChangeTracker.Clear();}
        }
        if(prepared.Count>0)
        {
            IReadOnlyList<WaterAiClient.BatchOutcome> estimates=[];
            try
            {
                // On partial retries use a manual transport subset; the persisted firmware mode is unchanged.
                string transportMode=timeline.Mode=="scheduled"&&prepared.Count==3?"scheduled":"manual";
                estimates=await ai.AnalyzeBatch(areaId,$"{stationId:N}:{bootId}:{batchId}",transportMode,
                    prepared.Select(p=>p.Prepared.Sample!).ToArray(),ct);
            }
            catch(AppException ex) when(ex.StatusCode is 400 or 502 or 503)
            {foreach(var p in prepared)Reject(p.Item,ex.StatusCode,ex.Message);}
            foreach(var p in prepared)
            {
                if(outcomes.ContainsKey(p.Item.Analyte))continue;
                ct.ThrowIfCancellationRequested();
                var outcome=estimates.Single(x=>x.Analyte==p.Item.Analyte);
                if(outcome.Estimate is not { } estimate)
                {
                    Reject(p.Item,outcome.ErrorStatus??502,"AI chưa thể đánh giá mẫu này.");continue;
                }
                try
                {
                    CompleteEstimate(p.Prepared,estimate);
                    var saved=await PersistAssessment(areaId,p.Prepared.Row,ct);
                    Accept(p.Item,saved);
                }
                catch(AppException ex) when(ex.StatusCode is 400 or 404 or 409 or 502 or 503)
                {Reject(p.Item,ex.StatusCode,ex.Message);}
                finally{db.ChangeTracker.Clear();}
            }
        }
        return Ok(ApiResponse<object>.Ok(new{batchId,mode=timeline.Mode,
            status=succeeded==outcomes.Count?"completed":succeeded>0?"partial":"failed",
            results=request.Samples.Select(s=>outcomes[s.Analyte]).ToArray(),productionPublished=false,chemicalResultValidated=false}));
    }
    [HttpPost("{analyte}/assessment")]
    [Authorize(Roles=AppRoles.FarmManage)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Assess(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,AssessRequest request,CancellationToken ct)
    {
        var prepared=await PrepareAssessment(areaId,stationId,bootId,batchId,analyte,request,ct);
        if(prepared.Existing)return Ok(ApiResponse<object>.Ok(View(prepared.Row)));
        if(prepared.Sample is { } s)
        {
            var estimate=await ai.Analyze(areaId,s.CameraId,s.Analyte,s.Unit,s.Lot,s.Token,s.CalibrationId,s.Photo,ct,s.Algorithm);
            CompleteEstimate(prepared,estimate);
        }
        return Ok(ApiResponse<object>.Ok(View(await PersistAssessment(areaId,prepared.Row,ct))));
    }
    private sealed record PreparedAssessment(WaterBenchResultAssessment Row,bool Existing=false,WaterAiClient.BatchSample? Sample=null,
        string? KitRevision=null,string? ImageSha256=null,string? Source=null,uint? ReactionStartMs=null,int? ReactionDurationMs=null);
    private async Task<PreparedAssessment> PrepareAssessment(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,
        AssessRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        if(request.RequestId==Guid.Empty||request.Action is not ("estimate" or "needs_retest")||
            string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>500||request.ReagentLot?.Length>128)
            throw AppException.BadRequest("Yêu cầu đánh giá không hợp lệ.");
        var q=Scoped(areaId,stationId,bootId,batchId);
        var timeline=await Timeline(q,bootId,batchId,ct);
        var c=timeline.Channels.SingleOrDefault(x=>x.Analyte==analyte)
            ??throw AppException.NotFound("Water bench analyte");
        if(timeline.Status=="running_observed"||c.SessionId is null)
            throw AppException.Conflict("Phiên chưa kết thúc hoặc chưa có session.");
        var session=await q.SingleOrDefaultAsync(x=>x.Kind=="session"&&x.Analyte==analyte&&x.FirmwareSessionId==c.SessionId,ct)
            ??throw AppException.Conflict("Thiếu bằng chứng session.");
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,WaterBatchPlanning.Json)))).ToLowerInvariant();
        var prior=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct);
        if(prior!=null)return new(Existing(prior,request.RequestId,hash),true);
        var row=new WaterBenchResultAssessment{SessionEvidenceId=session.Id,RequestId=request.RequestId,AssessedBy=user.UserId,
            RequestHash=hash,Reason=request.Reason.Trim(),Status="needs_retest"};
        if(request.Action=="estimate")
        {
            if(!WaterBenchResultPolicy.ReadyForEstimate(timeline,c)||!request.OperatorConditionsConfirmed||
                request.CalibrationId is not Guid calibration||calibration==Guid.Empty||
                request.ReactionSeconds is not int seconds||request.SalinityPpt is not decimal salinity||string.IsNullOrWhiteSpace(request.ReagentLot))
                throw AppException.Conflict("Cần phiên hoàn tất, điều kiện mẫu được xác nhận và hiệu chuẩn.");
            string source=WaterBatchPlanning.Source(request.SampleSource);
            var receiptRows=await q.Where(x=>x.Kind=="jpeg"&&x.ConnectionId==timeline.ConnectionId&&x.FirmwareSessionId==c.SessionId&&
                x.Analyte==analyte&&x.CaptureId==c.CaptureId).Take(2).ToListAsync(ct);
            if(receiptRows.Count!=1)throw AppException.Conflict("Chưa có đúng một biên nhận ảnh của lần chụp.");
            var receiptRow=receiptRows[0];var receipt=WaterBenchResultPolicy.VerifiedReceipt(receiptRow);
            if(receipt.CalibrationId!=calibration||receipt.CameraId is null||receipt.ConfigurationVersion is not >0)
                throw AppException.Conflict("Hiệu chuẩn không khớp biên nhận ảnh.");
            var profile=await db.WaterColorCalibrations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==calibration&&x.FarmingAreaId==areaId,ct)
                ??throw AppException.NotFound("Water color calibration");
            var observed=session.KioskReceivedAt??throw AppException.Conflict("Thiếu thời gian quan sát.");
            WaterBenchResultPolicy.Conditions(profile,analyte,observed,DateTime.UtcNow,request.ReagentLot,seconds,salinity);
            if(!WaterBenchResultPolicy.ReceiptMatches(receipt,profile.Id,profile.CameraDeviceId)||!await db.Devices.AnyAsync(x=>x.Id==profile.CameraDeviceId&&x.FarmingAreaId==areaId,ct))
                throw AppException.Conflict("Camera ngoài khu.");
            var asset=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receipt.MediaId,ct)
                ??throw AppException.NotFound("Media");
            if(!WaterBenchResultPolicy.AssetMatches(asset,profile.CameraDeviceId,stationId,bootId,batchId,analyte,c.CaptureId!.Value))
                throw AppException.Conflict("Ảnh không thuộc camera và phiên này.");
            if(await Used(areaId,receipt.MediaId,receipt.Sha256,ct))throw AppException.Conflict("Ảnh đã sử dụng cho mẫu khác hoặc hiệu chuẩn.");
            var content=await media.OpenContentAsync(receipt.MediaId,ct);
            await using var input=content.Content;
            using var buffer=new MemoryStream();var chunk=new byte[65536];int n;
            while((n=await input.ReadAsync(chunk.AsMemory(),ct))>0)
            {if(buffer.Length+n>5*1024*1024)throw AppException.BadRequest("Ảnh quá lớn.");await buffer.WriteAsync(chunk.AsMemory(0,n),ct);}
            var bytes=buffer.ToArray();
            if(bytes.LongLength!=asset.SizeBytes||!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)),receipt.Sha256,StringComparison.OrdinalIgnoreCase))
                throw AppException.Conflict("Hash hoặc kích thước ảnh không khớp biên nhận.");
            row.ImageEvidenceId=receiptRow.Id;row.MediaId=receipt.MediaId;row.CalibrationId=profile.Id;
            return new(row,false,new(profile.CameraDeviceId,analyte,profile.Unit,profile.ReagentLot,
                profile.AiCalibrationToken!,profile.AiCalibrationId!,new(receipt.MediaId,bytes,seconds,salinity),profile.AiAlgorithm!),
                profile.KitRevision,receipt.Sha256,source,c.ReactionStartMs,c.ReactionDurationMs);
        }
        else if(request.CalibrationId!=null||request.ReagentLot!=null||request.ReactionSeconds!=null||request.SalinityPpt!=null||
            request.SampleSource!=null||request.OperatorConditionsConfirmed)
            throw AppException.BadRequest("Yêu cầu đo lại chỉ nhận lý do.");
        return new(row);
    }
    private static void CompleteEstimate(PreparedAssessment p,WaterAiClient.Estimate estimate)
    {
        var s=p.Sample??throw new InvalidOperationException("Missing prepared sample.");
        var row=p.Row;
        row.Status="estimate_review_required";row.CandidateValue=estimate.Value;row.Unit=s.Unit;row.HoldoutError=estimate.HoldoutError;
        row.EvidenceJson=JsonSerializer.Serialize(new{algorithm=s.Algorithm,AiCalibrationId=s.CalibrationId,p.KitRevision,
            imageSha256=p.ImageSha256,reading=estimate.Evidence,source=p.Source,reagentLot=s.Lot,
            seconds=s.Photo.ReactionSeconds,salinity=s.Photo.SalinityPpt,
            conditionsSource="operator_attested",reactionClock="firmware_boot_uptime",p.ReactionStartMs,p.ReactionDurationMs,
            physicalExposureVerified=false,chemicalResultValidated=false,ph=(decimal?)null,temperatureC=(decimal?)null,
            ammoniaConversionApplied=false},WaterBatchPlanning.Json);
        if(Encoding.UTF8.GetByteCount(row.EvidenceJson)>65536)throw AppException.Conflict("Bằng chứng AI quá lớn.");
    }
    private async Task<WaterBenchResultAssessment> PersistAssessment(Guid areaId,WaterBenchResultAssessment row,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchEvidence\" WHERE \"Id\"={row.SessionEvidenceId} FOR UPDATE",ct);
        var prior=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==row.SessionEvidenceId,ct);
        if(prior!=null)return Existing(prior,row.RequestId,row.RequestHash);
        if(row.CalibrationId is Guid cal)
        {
            var now=DateTime.UtcNow;
            var locked=await db.WaterColorCalibrations.Where(x=>x.Id==cal&&x.FarmingAreaId==areaId&&x.RevokedAt==null&&
                x.ValidUntil>now&&(x.QualityCheckDueAt==null||x.QualityCheckDueAt>now))
                .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UpdatedAt,now),ct);
            if(locked!=1)throw AppException.Conflict("Hiệu chuẩn không còn hợp lệ.");
            using var evidence=JsonDocument.Parse(row.EvidenceJson);
            if(await Used(areaId,row.MediaId!.Value,evidence.RootElement.GetProperty("imageSha256").GetString()!,ct))
                throw AppException.Conflict("Ảnh đã được sử dụng.");
        }
        db.WaterBenchResultAssessments.Add(row);
        db.OperationLogs.Add(new OperationLog{UserId=user.UserId,Action="AssessWaterBenchResult",EntityType="WaterBenchResultAssessment",
            EntityId=row.Id,Details=row.Status,IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString()});
        try{await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(DbUpdateException){throw AppException.Conflict("Session hoặc ảnh đã được đánh giá.");}
        return row;
    }
    private static WaterBenchResultAssessment Existing(WaterBenchResultAssessment row,Guid request,string hash)
    {
        if(row.RequestId!=request||row.RequestHash!=hash)throw AppException.Conflict("Session đã được đánh giá; dùng phiên mới nếu cần đo lại.");
        return row;
    }
    private async Task<bool> Used(Guid area,Guid mediaId,string hash,CancellationToken ct)=>
        await db.WaterAnalysisRuns.AnyAsync(x=>x.SampleMediaId==mediaId||x.FarmingAreaId==area&&x.SampleSha256==hash,ct)||
        await db.WaterCalibrationChecks.AnyAsync(x=>x.MediaId==mediaId||x.ImageSha256==hash,ct)||
        await db.WaterTrainingSamples.AnyAsync(x=>x.MediaId==mediaId||x.FarmingAreaId==area&&x.ImageSha256==hash,ct)||
        await db.WaterBenchResultAssessments.AnyAsync(x=>x.MediaId==mediaId,ct);
}
