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
        a.Id,a.RequestId,a.SessionEvidenceId,a.ImageEvidenceId,a.Status,a.Reason,a.MediaId,a.CalibrationId,
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
    // Read-only chain verification, not a new inference or a storage download.
    [HttpGet("{analyte}/evidence")]
    public async Task<IActionResult> ResultEvidence(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        Response.Headers["Cache-Control"]="no-store";
        var q=Scoped(areaId,stationId,bootId,batchId);
        var timeline=await Timeline(q,bootId,batchId,ct);
        var c=timeline.Channels.SingleOrDefault(x=>x.Analyte==analyte)??throw AppException.NotFound("Water bench analyte");
        if(c.SessionId==null)throw AppException.NotFound("Session evidence");
        var session=await q.SingleOrDefaultAsync(x=>x.Kind=="session"&&x.Analyte==analyte&&
            x.FirmwareSessionId==c.SessionId&&x.ConnectionId==timeline.ConnectionId,ct)
            ??throw AppException.NotFound("Session evidence");
        var sessionReceipt=WaterBenchReceiptPolicy.Read(session);
        var row=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct)
            ??throw AppException.NotFound("Water bench assessment");
        if(row.Status=="needs_retest")
            return Ok(ApiResponse<object>.Ok(new{assessmentId=row.Id,row.RequestId,row.Status,session=sessionReceipt,
                provenanceStatus="no_inference",mediaContentRechecked=false,chemicalResultValidated=false,productionPublished=false}));
        if(!WaterBenchResultPolicy.ReadyForEstimate(timeline,c))throw AppException.Conflict("Incomplete assessment timeline.");
        var image=await q.SingleOrDefaultAsync(x=>x.Id==row.ImageEvidenceId&&x.Kind=="jpeg"&&x.Analyte==analyte&&
            x.FirmwareSessionId==c.SessionId&&x.ConnectionId==timeline.ConnectionId&&x.CaptureId==c.CaptureId,ct)
            ??throw AppException.Conflict("Assessment image linkage missing.");
        var provenance=WaterBenchAiEvidence.Verify(row,session,image);
        var receipt=WaterBenchResultPolicy.VerifiedReceipt(image);
        var profile=await db.WaterColorCalibrations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.CalibrationId&&
            x.FarmingAreaId==areaId,ct)??throw AppException.Conflict("Calibration linkage missing.");
        if(!WaterBenchResultPolicy.ReceiptMatches(receipt,profile.Id,profile.CameraDeviceId))
            throw AppException.Conflict("Calibration linkage mismatch.");
        var asset=await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.MediaId,ct)
            ??throw AppException.Conflict("Media linkage missing.");
        if(!WaterBenchResultPolicy.AssetMatches(asset,profile.CameraDeviceId,stationId,bootId,batchId,analyte,c.CaptureId!.Value))
            throw AppException.Conflict("Private media linkage mismatch.");
        return Ok(ApiResponse<object>.Ok(new{assessmentId=row.Id,row.RequestId,row.Status,row.CandidateValue,row.Unit,row.HoldoutError,
            session=sessionReceipt,image=WaterBenchReceiptPolicy.Read(image),provenance,
            provenanceStatus=provenance==null?"legacy_linkage_only":"snapshot_verified",
            provenanceSha256=provenance==null?null:WaterBenchAiEvidence.Digest(provenance),
            calibration=new{profile.Id,profile.CameraDeviceId,profile.RevokedAt,profile.ValidUntil,
                currentlyUsable=profile.RevokedAt==null&&profile.ValidUntil>DateTime.UtcNow&&
                    (profile.QualityCheckDueAt==null||profile.QualityCheckDueAt>DateTime.UtcNow)},
            mediaContentRechecked=false,chemicalResultValidated=false,productionPublished=false}));
    }
    public sealed record AmmoniaRequest(Guid RequestId,string Method,Guid SalinityMeasurementId,
        bool SameSampleSourceConfirmed,bool MeasurandConfirmed,string Reason);
    [HttpGet("NH3/ammonia-reference")]
    public async Task<IActionResult> GetAmmoniaReference(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        Response.Headers["Cache-Control"]="no-store";
        var q=Scoped(areaId,stationId,bootId,batchId);
        var row=await (from r in db.WaterBenchResultAssessments.AsNoTracking()
            join e in q on r.SessionEvidenceId equals e.Id where e.Kind=="session"&&e.Analyte=="NH3" select r).SingleOrDefaultAsync(ct)
            ??throw AppException.NotFound("NH3 assessment");
        using var doc=JsonDocument.Parse(row.EvidenceJson);
        if(!doc.RootElement.TryGetProperty("ammoniaReference",out var reference))
            return Ok(ApiResponse<object>.Ok(new{status="not_assessed",chemicalResultValidated=false,productionPublished=false}));
        if(doc.RootElement.GetProperty("ammoniaReferenceSha256").GetString()!=WaterBenchAiEvidence.Hash(reference.GetRawText()))
            throw AppException.Conflict("Ammonia reference digest mismatch.");
        return Ok(ApiResponse<object>.Ok(reference.Clone()));
    }
    [HttpPost("NH3/ammonia-reference")]
    [Authorize(Roles=AppRoles.FarmManage)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> AssessAmmoniaReference(Guid areaId,Guid stationId,string bootId,string batchId,AmmoniaRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        if(request.RequestId==Guid.Empty||request.SalinityMeasurementId==Guid.Empty||!request.SameSampleSourceConfirmed||
            !request.MeasurandConfirmed||string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>500||
            request.Method is not ("printed_table" or "equilibrium"))
            throw AppException.BadRequest("Explicit source and measurand confirmation required.");
        var q=Scoped(areaId,stationId,bootId,batchId);
        var timeline=await Timeline(q,bootId,batchId,ct);
        var channel=timeline.Channels.SingleOrDefault(x=>x.Analyte=="NH3")??throw AppException.NotFound("NH3");
        if(!WaterBenchResultPolicy.ReadyForEstimate(timeline,channel))throw AppException.Conflict("Incomplete NH3 timeline.");
        var session=await q.SingleAsync(x=>x.Kind=="session"&&x.Analyte=="NH3"&&x.ConnectionId==timeline.ConnectionId&&
            x.FirmwareSessionId==channel.SessionId,ct);
        var row=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct)
            ??throw AppException.NotFound("NH3 assessment");
        var requestHash=WaterBenchAiEvidence.Hash(JsonSerializer.Serialize(request,WaterBatchPlanning.Json));
        JsonElement? ExistingReference(WaterBenchResultAssessment r)
        {
            using var d=JsonDocument.Parse(r.EvidenceJson);
            if(!d.RootElement.TryGetProperty("ammoniaReference",out var existing))return null;
            if(d.RootElement.GetProperty("ammoniaReferenceSha256").GetString()!=WaterBenchAiEvidence.Hash(existing.GetRawText())||
                existing.GetProperty("requestId").GetGuid()!=request.RequestId||existing.GetProperty("requestSha256").GetString()!=requestHash)
                throw AppException.Conflict("Reference already recorded or digest mismatch.");
            return existing.Clone();
        }
        if(ExistingReference(row) is {} existing)return Ok(ApiResponse<object>.Ok(existing));
        var image=await q.SingleOrDefaultAsync(x=>x.Id==row.ImageEvidenceId&&x.Kind=="jpeg"&&x.CaptureId==channel.CaptureId,ct)
            ??throw AppException.Conflict("Missing NH3 capture receipt.");
        var provenance=WaterBenchAiEvidence.Verify(row,session,image)??throw AppException.Conflict("AI provenance snapshot required.");
        var sampleRows=await q.Where(x=>x.Kind=="sample_evidence"&&x.Analyte=="NH3"&&x.ConnectionId==timeline.ConnectionId&&
            x.FirmwareSessionId==channel.SessionId).Take(2).ToListAsync(ct);
        var linkRows=await q.Where(x=>x.Kind=="nh3_image_link"&&x.Analyte=="NH3"&&x.ConnectionId==timeline.ConnectionId&&
            x.FirmwareSessionId==channel.SessionId&&x.CaptureId==channel.CaptureId).Take(2).ToListAsync(ct);
        if(sampleRows.Count!=1||linkRows.Count!=1)throw AppException.Conflict("Missing or ambiguous NH3 observations.");
        var sample=sampleRows[0];var link=linkRows[0];
        _=WaterBenchReceiptPolicy.Read(sample);_=WaterBenchReceiptPolicy.Read(link);
        var observation=JsonSerializer.Deserialize<Nh3EvidenceEvent>(sample.PayloadJson,WaterBatchPlanning.Json)!;
        var salinity=await (from m in db.WaterMeasurements.AsNoTracking()
            join sensor in db.Sensors.AsNoTracking() on m.SensorId equals sensor.Id
            join d in db.Devices.AsNoTracking() on sensor.DeviceId equals (Guid?)d.Id
            where m.Id==request.SalinityMeasurementId&&sensor.IsActive&&sensor.SensorType=="Salinity"&&sensor.Unit=="ppt"&&
                sensor.FarmingAreaId==areaId&&d.FarmingAreaId==areaId&&d.EdgeState!="Revoked"
            select m).SingleOrDefaultAsync(ct)??throw AppException.Conflict("Salinity measurement outside permitted area or sensor mapping.");
        var calibration=await db.SensorCalibrations.AsNoTracking().Where(x=>x.SensorId==salinity.SensorId&&x.PerformedAt<=salinity.MeasuredAt)
            .OrderByDescending(x=>x.PerformedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
        if(calibration==null||calibration.Result!="Passed"||calibration.Unit!="ppt"||
            calibration.PerformedAt<salinity.MeasuredAt.AddDays(-30))
            throw AppException.Conflict("Recent successful salinity calibration required.");
        var env=WaterBenchAmmoniaPolicy.Environment(observation,salinity,DateTimeOffset.UtcNow);
        WaterBenchAmmoniaPolicy.Method(request.Method,row.Unit!,env);
        var profile=await db.WaterColorCalibrations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.CalibrationId&&x.FarmingAreaId==areaId,ct)
            ??throw AppException.Conflict("Calibration unavailable.");
        if(profile.RevokedAt!=null||profile.KitName!="Thủy Phước"||profile.Unit!=row.Unit||profile.AiCalibrationId!=provenance.AiCalibrationId)
            throw AppException.Conflict("NH3 calibration or measurand mismatch.");
        var result=await ai.AmmoniaReference(request.Method,row.Id,provenance.MediaId,row.CandidateValue!.Value,row.Unit!,env,ct);
        var evidence=JsonSerializer.SerializeToElement(new{contract="water-bench-ammonia-reference-v1",request.RequestId,requestSha256=requestHash,
            assessmentId=row.Id,provenanceSha256=WaterBenchAiEvidence.Digest(provenance),inputValue=row.CandidateValue,inputUnit=row.Unit,
            sampleReceiptId=sample.Id,samplePayloadSha256=sample.PayloadSha256,imageLinkReceiptId=link.Id,imageLinkPayloadSha256=link.PayloadSha256,
            salinityMeasurementId=salinity.Id,salinity.SensorId,salinity.EdgeEventKey,salinityCalibrationId=calibration.Id,
            environment=env,result,confirmedBy=user.UserId,reason=request.Reason.Trim(),recordedAt=DateTimeOffset.UtcNow,
            sameSampleSourceOperatorAttested=true,measurandOperatorAttested=true,clockDriftValidated=false,sensorVialAlignmentVerified=false,
            chemicalResultValidated=false,crabToxicityThresholdValidated=false,productionPublished=false},WaterBatchPlanning.Json);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await LockSession(session.Id,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchResultAssessments\" WHERE \"Id\"={row.Id} FOR UPDATE",ct);
        var current=await db.WaterBenchResultAssessments.SingleAsync(x=>x.Id==row.Id,ct);
        if(ExistingReference(current) is {} concurrent)return Ok(ApiResponse<object>.Ok(concurrent));
        if(current.EvidenceJson!=row.EvidenceJson||current.CandidateValue!=row.CandidateValue||current.Unit!=row.Unit)
            throw AppException.Conflict("Assessment changed.");
        // Append only; retain the exact existing optical evidence bytes and checksum.
        string original=current.EvidenceJson.TrimEnd();
        if(!original.EndsWith("}",StringComparison.Ordinal))throw AppException.Conflict("Invalid stored evidence.");
        string referenceText=evidence.GetRawText();
        current.EvidenceJson=original[..^1]+",\"ammoniaReference\":"+referenceText+
            ",\"ammoniaReferenceSha256\":\""+WaterBenchAiEvidence.Hash(referenceText)+"\"}";
        if(Encoding.UTF8.GetByteCount(current.EvidenceJson)>65536)throw AppException.Conflict("Evidence capacity exceeded.");
        db.OperationLogs.Add(new OperationLog{UserId=user.UserId,Action="RecordWaterBenchAmmoniaReference",EntityType="WaterBenchResultAssessment",
            EntityId=row.Id,Details=request.Method,IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString()});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(evidence));
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
        var prepared=new List<(BatchAssessItem Item,PreparedAssessment Prepared,Guid AttemptId)>();
        int succeeded=0;
        void Reject(BatchAssessItem item,int status,string message)=>outcomes[item.Analyte]=new
        {
            item.Analyte,requestId=item.Assessment.RequestId,status="assessment_rejected",httpStatus=status,
            retryable=status==503,message
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
                else
                {
                    var reserved=await ReserveAttempt(areaId,p.Row,item.Assessment,ct);
                    if(reserved.Existing is {} prior)Accept(item,prior);
                    else prepared.Add((item,p,reserved.AttemptId));
                }
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
            {
                foreach(var p in prepared)
                {
                    await FailAttempt(p.Prepared.Row.SessionEvidenceId,p.AttemptId,ex.StatusCode,ct);
                    Reject(p.Item,ex.StatusCode,ex.Message);
                }
            }
            foreach(var p in prepared)
            {
                if(outcomes.ContainsKey(p.Item.Analyte))continue;
                ct.ThrowIfCancellationRequested();
                var outcome=estimates.Single(x=>x.Analyte==p.Item.Analyte);
                if(outcome.Estimate is not { } estimate)
                {
                    int error=outcome.ErrorStatus??502;
                    await FailAttempt(p.Prepared.Row.SessionEvidenceId,p.AttemptId,error,ct);
                    Reject(p.Item,error,"AI chưa thể đánh giá mẫu này.");continue;
                }
                try
                {
                    CompleteEstimate(p.Prepared,estimate);
                    var saved=await PersistAssessment(areaId,p.Prepared.Row,ct,p.AttemptId);
                    Accept(p.Item,saved);
                }
                catch(AppException ex) when(ex.StatusCode is 400 or 404 or 409 or 502 or 503)
                {
                    db.ChangeTracker.Clear();
                    await FailAttempt(p.Prepared.Row.SessionEvidenceId,p.AttemptId,ex.StatusCode,ct);
                    Reject(p.Item,ex.StatusCode,ex.Message);
                }
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
            var reserved=await ReserveAttempt(areaId,prepared.Row,request,ct);
            if(reserved.Existing is {} prior)return Ok(ApiResponse<object>.Ok(View(prior)));
            try
            {
                var estimate=await ai.Analyze(areaId,s.CameraId,s.Analyte,s.Unit,s.Lot,s.Token,s.CalibrationId,s.Photo,ct,s.Algorithm);
                CompleteEstimate(prepared,estimate);
                return Ok(ApiResponse<object>.Ok(View(await PersistAssessment(areaId,prepared.Row,ct,reserved.AttemptId))));
            }
            catch(AppException ex)
            {
                db.ChangeTracker.Clear();
                await FailAttempt(prepared.Row.SessionEvidenceId,reserved.AttemptId,ex.StatusCode,ct);
                throw;
            }
        }
        return Ok(ApiResponse<object>.Ok(View(await PersistAssessment(areaId,prepared.Row,ct))));
    }
    [HttpGet("{analyte}/retry-request")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> RetryRequest(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        Response.Headers["Cache-Control"]="no-store";
        var session=await AdminSession(areaId,stationId,bootId,batchId,analyte,ct);
        if(await db.WaterBenchResultAssessments.AnyAsync(x=>x.SessionEvidenceId==session.Id,ct))
            throw AppException.Conflict("Session already assessed.");
        var last=await LatestAttempt(session.Id,ct)??throw AppException.NotFound("AI attempt");
        if(WaterBenchAiRetry.Status(last,DateTime.UtcNow)!="retry_available")
            throw AppException.Conflict("Retry is not available.");
        if(last.RequestJson==null)throw AppException.Conflict("Legacy attempt has no recoverable body; use original request.");
        if(WaterBenchAiEvidence.Hash(last.RequestJson)!=last.RequestHash)
            throw AppException.Conflict("Retry request checksum mismatch.");
        var request=JsonSerializer.Deserialize<AssessRequest>(last.RequestJson,WaterBatchPlanning.Json);
        if(request==null||request.RequestId!=last.RequestId||request.Action!="estimate")
            throw AppException.Conflict("Invalid stored retry request.");
        return Ok(ApiResponse<object>.Ok(request));
    }
    private async Task<WaterBenchEvidence> AdminSession(Guid area,Guid station,string boot,string batch,string analyte,CancellationToken ct)
    {
        var q=Scoped(area,station,boot,batch);
        var timeline=await Timeline(q,boot,batch,ct);
        var channel=timeline.Channels.SingleOrDefault(x=>x.Analyte==analyte)??throw AppException.NotFound("Analyte");
        return await q.SingleOrDefaultAsync(x=>x.Kind=="session"&&x.Analyte==analyte&&x.ConnectionId==timeline.ConnectionId&&
            x.FirmwareSessionId==channel.SessionId,ct)??throw AppException.NotFound("Session");
    }
    [HttpGet("NH3/salinity-candidates")]
    [Authorize(Roles=AppRoles.FarmManage)]
    public async Task<IActionResult> SalinityCandidates(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        Response.Headers["Cache-Control"]="no-store";
        var session=await AdminSession(areaId,stationId,bootId,batchId,"NH3",ct);
        var q=Scoped(areaId,stationId,bootId,batchId);
        var sample=await q.SingleOrDefaultAsync(x=>x.Kind=="sample_evidence"&&x.Analyte=="NH3"&&x.ConnectionId==session.ConnectionId&&
            x.FirmwareSessionId==session.FirmwareSessionId,ct)??throw AppException.Conflict("NH3 sample evidence unavailable.");
        _=WaterBenchReceiptPolicy.Read(sample);
        var observation=JsonSerializer.Deserialize<Nh3EvidenceEvent>(sample.PayloadJson,WaterBatchPlanning.Json)!;
        var at=observation.ClockEstimate?.EstimatedSampleUtc??throw AppException.Conflict("Sample clock unavailable.");
        var start=at.UtcDateTime.AddSeconds(-10);var end=at.UtcDateTime.AddSeconds(10);
        var candidates=await (from m in db.WaterMeasurements.AsNoTracking()
            join sensor in db.Sensors.AsNoTracking() on m.SensorId equals sensor.Id
            join d in db.Devices.AsNoTracking() on sensor.DeviceId equals (Guid?)d.Id
            where sensor.IsActive&&sensor.SensorType=="Salinity"&&sensor.Unit=="ppt"&&sensor.FarmingAreaId==areaId&&
                d.FarmingAreaId==areaId&&d.EdgeState!="Revoked"&&m.Unit=="ppt"&&m.DataQuality=="Realtime"&&m.MeasuredAt>=start&&m.MeasuredAt<=end
            orderby m.MeasuredAt,m.Id
            select new{Measurement=m,sensor.SensorCode,sensor.InstallationLocation}).Take(200).ToListAsync(ct);
        var items=new List<object>();
        foreach(var candidate in candidates)
        {
            var m=candidate.Measurement;
            var cal=await db.SensorCalibrations.AsNoTracking().Where(x=>x.SensorId==m.SensorId&&x.PerformedAt<=m.MeasuredAt)
                .OrderByDescending(x=>x.PerformedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
            if(cal==null||cal.Result!="Passed"||cal.Unit!="ppt"||cal.PerformedAt<m.MeasuredAt.AddDays(-30))continue;
            try
            {
                var env=WaterBenchAmmoniaPolicy.Environment(observation,m,DateTimeOffset.UtcNow);
                items.Add(new{measurementId=m.Id,m.SensorId,candidate.SensorCode,candidate.InstallationLocation,m.Value,m.Unit,m.MeasuredAt,
                    differenceSeconds=(env.SalinityAt-env.SampleAt).TotalSeconds,calibrationId=cal.Id,cal.PerformedAt});
            }
            catch(AppException){/* Not eligible; write endpoint independently rechecks. */}
        }
        return Ok(ApiResponse<object>.Ok(new{sampleAt=at,items,sourceConfirmationRequired=true,clockDriftValidated=false,limited=candidates.Count==200}));
    }
    public sealed record ReviewRequest(Guid RequestId,string Decision,string Reason,Guid? ReferenceMediaId=null,
        decimal? ReferenceValue=null,string? ReferenceUnit=null,string? ReferenceMethod=null,bool SameSampleConfirmed=false);
    public sealed record PublishRequest(Guid RequestId,string Reason);
    [HttpGet("{analyte}/review")]
    public async Task<IActionResult> GetReview(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        Response.Headers["Cache-Control"]="no-store";
        var session=await AdminSession(areaId,stationId,bootId,batchId,analyte,ct);
        var row=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct)
            ??throw AppException.NotFound("Assessment");
        var review=WaterBenchReviewPolicy.Read(row.EvidenceJson,"review");
        var publication=WaterBenchReviewPolicy.Read(row.EvidenceJson,"publication");
        if(publication!=null&&(review==null||publication.Value.GetProperty("reviewSha256").GetString()!=WaterBenchAiEvidence.Hash(review.Value.GetRawText())))
            throw AppException.Conflict("Publication linkage mismatch.");
        return Ok(ApiResponse<object>.Ok(new{assessmentId=row.Id,review,publication,status=publication!=null?"published":review?.GetProperty("decision").GetString()??"pending_review",
            publicationScope="water_bench_reviewed_record",productionSensorPublished=false,chemicalAccuracyIndependentlyVerifiedBySystem=false}));
    }
    [HttpPost("{analyte}/review")]
    [Authorize(Roles=AppRoles.FarmManage)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Review(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,ReviewRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        WaterBenchReviewPolicy.Validate(request,analyte);
        var session=await AdminSession(areaId,stationId,bootId,batchId,analyte,ct);
        var requestHash=WaterBenchAiEvidence.Hash(JsonSerializer.Serialize(request,WaterBatchPlanning.Json));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await LockSession(session.Id,ct);
        var row=await db.WaterBenchResultAssessments.SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct)
            ??throw AppException.NotFound("Assessment");
        if(WaterBenchReviewPolicy.Replay(row.EvidenceJson,"review",request.RequestId,requestHash) is {} prior)
            return Ok(ApiResponse<object>.Ok(prior));
        var image=await db.WaterBenchEvidence.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==row.ImageEvidenceId,ct)
            ??throw AppException.Conflict("AI image missing.");
        var provenance=WaterBenchAiEvidence.Verify(row,session,image)??throw AppException.Conflict("Verified AI snapshot required.");
        string? referenceSha=null;
        if(request.Decision=="approved")
        {
            if(row.Unit!=request.ReferenceUnit)throw AppException.BadRequest("Reference must use identical measurand.");
            var asset=await (from a in db.MediaAssets.AsNoTracking() join d in db.Devices.AsNoTracking() on a.DeviceId equals (Guid?)d.Id
                where a.Id==request.ReferenceMediaId&&d.FarmingAreaId==areaId&&a.Category=="image"&&!a.IsShared&&a.SizeBytes>0&&a.SizeBytes<=5*1024*1024
                select a).SingleOrDefaultAsync(ct)??throw AppException.Conflict("Independent private reference image required.");
            if(asset.Id==row.MediaId||asset.Notes?.StartsWith("water-bench:",StringComparison.OrdinalIgnoreCase)==true||
                await Used(areaId,asset.Id,"",ct))throw AppException.Conflict("Reference must be independent of AI sample and calibration.");
            var content=await media.OpenContentAsync(asset.Id,ct);
            await using var stream=content.Content;using var buffer=new MemoryStream();var bytes=new byte[65536];int n;
            while((n=await stream.ReadAsync(bytes.AsMemory(),ct))>0)
            {if(buffer.Length+n>5*1024*1024)throw AppException.Conflict("Reference too large.");await buffer.WriteAsync(bytes.AsMemory(0,n),ct);}
            if(buffer.Length!=asset.SizeBytes)throw AppException.Conflict("Reference size mismatch.");
            referenceSha=Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
            if(referenceSha==provenance.ImageSha256||await Used(areaId,asset.Id,referenceSha,ct))throw AppException.Conflict("Reference reused AI evidence.");
        }
        var review=JsonSerializer.SerializeToElement(new{contract="water-bench-human-review-v1",request.RequestId,requestSha256=requestHash,
            assessmentId=row.Id,request.Decision,reason=request.Reason.Trim(),reviewedBy=user.UserId,reviewedAt=DateTime.UtcNow,
            provenanceSha256=WaterBenchAiEvidence.Digest(provenance),aiEstimate=row.CandidateValue,aiUnit=row.Unit,
            request.ReferenceMediaId,referenceSha256=referenceSha,request.ReferenceValue,request.ReferenceUnit,request.ReferenceMethod,
            sameSampleOperatorAttested=request.SameSampleConfirmed,chemicalAccuracyIndependentlyVerifiedBySystem=false},WaterBatchPlanning.Json);
        row.EvidenceJson=WaterBenchReviewPolicy.Append(row.EvidenceJson,"review",review);
        db.OperationLogs.Add(new OperationLog{UserId=user.UserId,Action="ReviewWaterBenchResult",EntityType="WaterBenchResultAssessment",
            EntityId=row.Id,Details=request.Decision,IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString()});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(review));
    }
    [HttpPost("{analyte}/publish")]
    [Authorize(Roles=AppRoles.FarmManage)]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Publish(Guid areaId,Guid stationId,string bootId,string batchId,string analyte,PublishRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,true,ct);
        if(request.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Length>500)throw AppException.BadRequest("Publication reason required.");
        var session=await AdminSession(areaId,stationId,bootId,batchId,analyte,ct);
        var hash=WaterBenchAiEvidence.Hash(JsonSerializer.Serialize(request,WaterBatchPlanning.Json));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await LockSession(session.Id,ct);
        var row=await db.WaterBenchResultAssessments.SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct)??throw AppException.NotFound("Assessment");
        if(WaterBenchReviewPolicy.Replay(row.EvidenceJson,"publication",request.RequestId,hash) is {} existing)return Ok(ApiResponse<object>.Ok(existing));
        var review=WaterBenchReviewPolicy.Read(row.EvidenceJson,"review")??throw AppException.Conflict("Review required.");
        if(review.GetProperty("decision").GetString()!="approved")throw AppException.Conflict("Approved independent reference required.");
        var image=await db.WaterBenchEvidence.AsNoTracking().SingleAsync(x=>x.Id==row.ImageEvidenceId,ct);
        var provenance=WaterBenchAiEvidence.Verify(row,session,image)??throw AppException.Conflict("AI evidence required.");
        if(review.GetProperty("provenanceSha256").GetString()!=WaterBenchAiEvidence.Digest(provenance))throw AppException.Conflict("Review evidence mismatch.");
        var publication=JsonSerializer.SerializeToElement(new{contract="water-bench-reviewed-publication-v1",request.RequestId,requestSha256=hash,
            assessmentId=row.Id,reviewSha256=WaterBenchAiEvidence.Hash(review.GetRawText()),publishedBy=user.UserId,publishedAt=DateTime.UtcNow,
            reason=request.Reason.Trim(),value=review.GetProperty("referenceValue").GetDecimal(),unit=review.GetProperty("referenceUnit").GetString(),
            source="operator_attested_independent_reference",publicationScope="water_bench_reviewed_record",
            productionSensorPublished=false,chemicalAccuracyIndependentlyVerifiedBySystem=false},WaterBatchPlanning.Json);
        row.EvidenceJson=WaterBenchReviewPolicy.Append(row.EvidenceJson,"publication",publication);
        db.OperationLogs.Add(new OperationLog{UserId=user.UserId,Action="PublishWaterBenchReviewedResult",EntityType="WaterBenchResultAssessment",
            EntityId=row.Id,Details="water_bench_reviewed_record",IpAddress=HttpContext.Connection.RemoteIpAddress?.ToString()});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(publication));
    }
    [HttpGet("retry-status")]
    public async Task<IActionResult> RetryStatus(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        Response.Headers["Cache-Control"]="no-store";
        var q=Scoped(areaId,stationId,bootId,batchId);
        var timeline=await Timeline(q,bootId,batchId,ct);
        var sessions=await q.Where(x=>x.Kind=="session"&&x.ConnectionId==timeline.ConnectionId).ToListAsync(ct);
        var channels=new List<object>();
        foreach(var c in timeline.Channels)
        {
            var session=sessions.SingleOrDefault(s=>s.Analyte==c.Analyte&&s.FirmwareSessionId==c.SessionId);
            if(session==null){channels.Add(new{c.Analyte,status="missing_session"});continue;}
            var result=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==session.Id,ct);
            var last=await db.WaterBenchAiAttempts.AsNoTracking().Where(x=>x.SessionEvidenceId==session.Id)
                .OrderByDescending(x=>x.Number).FirstOrDefaultAsync(ct);
            channels.Add(new{c.Analyte,c.SessionId,status=result?.Status??WaterBenchAiRetry.Status(last,DateTime.UtcNow),
                attempts=last?.Number??0,maxAttempts=WaterBenchAiRetry.MaxAttempts,requestId=last?.RequestId,
                leaseUntil=last?.LeaseUntil,retryAfter=last?.RetryAfter,hardwareReplay=false});
        }
        return Ok(ApiResponse<object>.Ok(new{channels,productionPublished=false}));
    }
    private Task<WaterBenchAiAttempt?> LatestAttempt(Guid session,CancellationToken ct)=>
        db.WaterBenchAiAttempts.Where(x=>x.SessionEvidenceId==session).OrderByDescending(x=>x.Number).FirstOrDefaultAsync(ct);
    private async Task LockSession(Guid session,CancellationToken ct)=>
        _=await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchEvidence\" WHERE \"Id\"={session} FOR UPDATE",ct);
    private async Task<(Guid AttemptId,WaterBenchResultAssessment? Existing)> ReserveAttempt(Guid area,WaterBenchResultAssessment row,AssessRequest request,CancellationToken ct)
    {
        await scope.EnsureAsync(area,true,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await LockSession(row.SessionEvidenceId,ct);
        var prior=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==row.SessionEvidenceId,ct);
        if(prior!=null)return (Guid.Empty,Existing(prior,row.RequestId,row.RequestHash));
        var last=await LatestAttempt(row.SessionEvidenceId,ct);
        var now=DateTime.UtcNow;
        WaterBenchAiRetry.EnsureRetry(last,row.RequestId,row.RequestHash,now);
        var attempt=new WaterBenchAiAttempt{SessionEvidenceId=row.SessionEvidenceId,RequestId=row.RequestId,RequestHash=row.RequestHash,
            Number=(last?.Number??0)+1,State="running",LeaseUntil=now+WaterBenchAiRetry.Lease,
            RequestJson=JsonSerializer.Serialize(request,WaterBatchPlanning.Json)};
        db.WaterBenchAiAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
        return (attempt.Id,null);
    }
    private async Task FailAttempt(Guid session,Guid id,int status,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await LockSession(session,ct);
        var last=await LatestAttempt(session,ct);
        if(last==null||last.Id!=id||last.State!="running")return;
        last.State=WaterBenchAiRetry.FailureState(status);last.UpdatedAt=DateTime.UtcNow;
        last.RetryAfter=last.State=="retryable"?DateTime.UtcNow+WaterBenchAiRetry.Delay(last.Number):null;
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);db.ChangeTracker.Clear();
    }
    private sealed record PreparedAssessment(WaterBenchResultAssessment Row,bool Existing=false,WaterAiClient.BatchSample? Sample=null,
        string? KitRevision=null,string? ImageSha256=null,string? Source=null,uint? ReactionStartMs=null,int? ReactionDurationMs=null,
        WaterBenchAiProvenance? Provenance=null);
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
        var session=await q.SingleOrDefaultAsync(x=>x.Kind=="session"&&x.Analyte==analyte&&x.FirmwareSessionId==c.SessionId&&x.ConnectionId==timeline.ConnectionId,ct)
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
                profile.KitRevision,receipt.Sha256,source,c.ReactionStartMs,c.ReactionDurationMs,
                WaterBenchAiEvidence.Build(row,session,receiptRow,profile.AiCalibrationId!,profile.AiAlgorithm!));
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
        var provenance=p.Provenance??throw new InvalidOperationException("Missing AI provenance.");
        row.EvidenceJson=JsonSerializer.Serialize(new{algorithm=s.Algorithm,AiCalibrationId=s.CalibrationId,p.KitRevision,
            provenance,provenanceSha256=WaterBenchAiEvidence.Digest(provenance),
            candidateValue=row.CandidateValue,unit=row.Unit,holdoutError=row.HoldoutError,
            readingSha256=WaterBenchAiEvidence.Hash(JsonSerializer.Serialize(estimate.Evidence,WaterBatchPlanning.Json)),
            imageSha256=p.ImageSha256,reading=estimate.Evidence,source=p.Source,reagentLot=s.Lot,
            seconds=s.Photo.ReactionSeconds,salinity=s.Photo.SalinityPpt,
            conditionsSource="operator_attested",reactionClock="firmware_boot_uptime",p.ReactionStartMs,p.ReactionDurationMs,
            physicalExposureVerified=false,chemicalResultValidated=false,ph=(decimal?)null,temperatureC=(decimal?)null,
            ammoniaConversionApplied=false},WaterBatchPlanning.Json);
        if(Encoding.UTF8.GetByteCount(row.EvidenceJson)>65536)throw AppException.Conflict("Bằng chứng AI quá lớn.");
    }
    private async Task<WaterBenchResultAssessment> PersistAssessment(Guid areaId,WaterBenchResultAssessment row,CancellationToken ct,Guid? attemptId=null)
    {
        await scope.EnsureAsync(areaId,true,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchEvidence\" WHERE \"Id\"={row.SessionEvidenceId} FOR UPDATE",ct);
        var prior=await db.WaterBenchResultAssessments.AsNoTracking().SingleOrDefaultAsync(x=>x.SessionEvidenceId==row.SessionEvidenceId,ct);
        if(prior!=null)return Existing(prior,row.RequestId,row.RequestHash);
        var attempt=await LatestAttempt(row.SessionEvidenceId,ct);
        if(attemptId is Guid lease)
        {
            if(!WaterBenchAiRetry.Owns(attempt,lease,DateTime.UtcNow))throw AppException.Conflict("AI attempt lease expired or superseded.");
            attempt!.State="succeeded";attempt.UpdatedAt=DateTime.UtcNow;
        }
        else if(attempt?.State=="running"&&attempt.LeaseUntil>DateTime.UtcNow)
            throw AppException.Conflict("AI inference is running; wait before marking retest.");
        if(row.CalibrationId is Guid cal)
        {
            var now=DateTime.UtcNow;
            var locked=await db.WaterColorCalibrations.Where(x=>x.Id==cal&&x.FarmingAreaId==areaId&&x.RevokedAt==null&&
                x.ValidUntil>now&&(x.QualityCheckDueAt==null||x.QualityCheckDueAt>now))
                .ExecuteUpdateAsync(u=>u.SetProperty(x=>x.UpdatedAt,now),ct);
            if(locked!=1)throw AppException.Conflict("Hiệu chuẩn không còn hợp lệ.");
            var session=await db.WaterBenchEvidence.AsNoTracking().SingleAsync(x=>x.Id==row.SessionEvidenceId,ct);
            var image=await db.WaterBenchEvidence.AsNoTracking().SingleAsync(x=>x.Id==row.ImageEvidenceId,ct);
            if(WaterBenchAiEvidence.Verify(row,session,image)==null)throw AppException.Conflict("Missing AI evidence snapshot.");
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
