using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/edge/water-bench")]
public sealed class WaterBenchMediaController(AppDbContext db,IKioskProvisioningService kiosks,
    IMediaStorageService storage,ILogger<WaterBenchMediaController> log):ControllerBase
{
    private sealed record StoredReceipt(Guid MediaId,string Sha256,string Contract,Guid CalibrationId,Guid CameraId,int ConfigurationVersion,
        string RequestHash,int Width,int Height,uint Crc32,uint Sequence,DateTimeOffset RequestedAt,DateTimeOffset ReceivedAt,
        bool ActualExposureVerified=false,bool ChemicalAccuracyValidated=false);
    [AllowAnonymous]
    [HttpPost("media")]
    [RequestSizeLimit(96*1024)]
    public async Task<IActionResult> Upload(BenchMediaUpload request,CancellationToken ct)
    {
        var secret=Request.Headers["X-Kiosk-Key"].FirstOrDefault()??"";
        var session=await kiosks.GetSessionAsync(secret,ct);
        var kioskId=session.Data?.KioskId??throw AppException.Unauthorized("Kiosk credential required.");
        var kiosk=await db.FarmKiosks.AsNoTracking().SingleOrDefaultAsync(k=>k.Id==kioskId&&k.Status!="Revoked",ct)
            ??throw AppException.Unauthorized("Kiosk revoked.");
        var bytes=WaterBenchMediaPolicy.Validate(request);
        var canonical=JsonSerializer.Serialize(request with{JpegBase64=""},WaterBatchPlanning.Json);
        string requestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Area then station lock: same order as configuration, prevents concurrent duplicate uploads.
        await WaterBenchConfiguration.LockArea(db,kiosk.FarmingAreaId,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchStations\" WHERE \"Id\"={request.StationId} AND \"KioskId\"={kioskId} FOR UPDATE",ct);
        var station=await db.WaterBenchStations.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==request.StationId&&s.KioskId==kioskId&&s.RevokedAt==null,ct)
            ??throw AppException.NotFound("Active water bench station");
        if(station.MasterHardwareId!=request.MasterHardwareId||station.SlaveHardwareId!=request.SlaveHardwareId)
            throw AppException.Conflict("Hardware identity mismatch.");
        if(!await db.FarmKiosks.AnyAsync(k=>k.Id==kioskId&&k.FarmingAreaId==kiosk.FarmingAreaId&&k.Status!="Revoked",ct))
            throw AppException.Unauthorized("Kiosk scope changed.");
        var events=db.WaterBenchEvidence.AsNoTracking().Where(x=>x.StationId==station.Id);
        var prior=await events.SingleOrDefaultAsync(x=>x.EventId==request.EventId,ct);
        if(prior!=null)
        {
            if(prior.Kind!="jpeg")throw AppException.Conflict("Event ID already used.");
            var r=JsonSerializer.Deserialize<StoredReceipt>(prior.PayloadJson,WaterBatchPlanning.Json);
            if(r is null||r.RequestHash!=requestHash||r.Sha256!=request.Sha256)throw AppException.Conflict("Event content changed.");
            return Ok(ApiResponse<object>.Ok(new{eventId=prior.EventId,receiptId=prior.Id,mediaId=r.MediaId,sha256=r.Sha256,duplicate=true}));
        }
        var batch=events.Where(x=>x.BootId==request.BootId&&x.FirmwareBatchId==request.BatchId);
        var rows=await batch.Where(x=>x.Kind=="batch_start"||x.Kind=="session"||x.Kind=="batch_status"||x.Kind=="image_request"||x.Kind=="operator_event")
            .Select(x=>x.PayloadJson).Take(WaterBenchBatchTimeline.MaxEvents+1).ToListAsync(ct);
        if(rows.Count==0||rows.Count>WaterBenchBatchTimeline.MaxEvents)throw AppException.Conflict("Batch observation unavailable.");
        var timeline=WaterBenchBatchTimeline.Replay(rows);
        var image=await batch.SingleOrDefaultAsync(x=>x.Kind=="image_request"&&x.ConnectionId==request.ConnectionId&&
            x.FirmwareSessionId==request.SessionId&&x.Analyte==request.Analyte&&x.CaptureId==request.CaptureId,ct)
            ??throw AppException.Conflict("Upload batch image request first.");
        var imageRequest=JsonSerializer.Deserialize<BenchBatchEvent>(image.PayloadJson,WaterBatchPlanning.Json)
            ??throw AppException.Conflict("Invalid stored image request.");
        WaterBenchMediaPolicy.Match(request,timeline,imageRequest);
        if(await batch.AnyAsync(x=>x.Kind=="jpeg"&&x.ConnectionId==request.ConnectionId&&x.FirmwareSessionId==request.SessionId&&
            x.Analyte==request.Analyte&&x.CaptureId==request.CaptureId,ct))
            throw AppException.Conflict("Capture already has a receipt; retry original event ID.");
        var hashFragment="\"sha256\":\""+request.Sha256+"\"";
        if(await (from e in db.WaterBenchEvidence.AsNoTracking()
                  join s in db.WaterBenchStations on e.StationId equals s.Id
                  join k in db.FarmKiosks on s.KioskId equals k.Id
                  where k.FarmingAreaId==kiosk.FarmingAreaId&&e.Kind=="jpeg"&&e.PayloadJson.Contains(hashFragment)
                  select e.Id).AnyAsync(ct)||
            await db.WaterAnalysisRuns.AnyAsync(x=>x.FarmingAreaId==kiosk.FarmingAreaId&&x.SampleSha256==request.Sha256,ct)||
            await db.WaterTrainingSamples.AnyAsync(x=>x.FarmingAreaId==kiosk.FarmingAreaId&&x.ImageSha256==request.Sha256,ct)||
            await db.WaterCalibrationChecks.AnyAsync(x=>x.ImageSha256==request.Sha256,ct))
            throw AppException.Conflict("JPEG bytes already used.");
        // No edge-supplied camera ID. Bind to the station's configured target at ingest.
        var targets=JsonSerializer.Deserialize<WaterBatchPlanning.Target[]>(station.TargetsJson,WaterBatchPlanning.Json)??[];
        var target=targets.SingleOrDefault(t=>t.Analyte==request.Analyte)
            ??throw AppException.Conflict("Configure station calibration target before uploading media.");
        var profile=await db.WaterColorCalibrations.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==target.CalibrationId&&p.Analyte==request.Analyte&&p.FarmingAreaId==kiosk.FarmingAreaId,ct)
            ??throw AppException.Conflict("Calibration camera mapping unavailable.");
        if(station.ConfigurationVersion<=0||profile.CreatedAt>request.RequestedAt.UtcDateTime||
            !await db.Devices.AnyAsync(d=>d.Id==profile.CameraDeviceId&&d.FarmingAreaId==kiosk.FarmingAreaId,ct))
            throw AppException.Conflict("Configured camera mapping is not applicable.");
        var area=await db.FarmingAreas.AsNoTracking().SingleAsync(a=>a.Id==kiosk.FarmingAreaId,ct);
        var folder=MediaStorageLayout.WaterBench(MediaFolderPath.Leaf(area.Id,area.Code),station.Id,request.BootId,request.BatchId,
            request.Analyte,request.SessionId,request.ReceivedAt.UtcDateTime);
        var name=$"capture-{request.CaptureId}-{request.EventId:N}.jpg";
        // Preserve configured storage abstraction, but this path requires Google Drive private root.
        if(storage.ProviderName!="GoogleDrive")throw AppException.Conflict("WaterBench requires configured private Google Drive storage.");
        MediaUploadResult? uploaded=null;bool commitAttempted=false;
        try
        {
            using var input=new MemoryStream(bytes,writable:false);
            uploaded=await storage.UploadAsync(input,name,"image/jpeg",folder,ct,publicRead:false);
            if(uploaded.SizeBytes!=bytes.LongLength||string.IsNullOrWhiteSpace(uploaded.StorageKey))
                throw AppException.Conflict("Storage receipt size mismatch.");
            await storage.EnsurePrivateAccessAsync(uploaded.StorageKey,ct);
            // Verify the bytes from the provider before committing the receipt.
            await using(var remote=await storage.DownloadAsync(uploaded.StorageKey,ct))
            {
                using var buffer=new MemoryStream();var chunk=new byte[8192];int count;
                while((count=await remote.ReadAsync(chunk.AsMemory(),ct))>0)
                {if(buffer.Length+count>WaterBenchMediaPolicy.MaxBytes)throw AppException.Conflict("Stored image too large.");buffer.Write(chunk,0,count);}
                if(buffer.Length!=bytes.LongLength||Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant()!=request.Sha256)
                    throw AppException.Conflict("Stored JPEG digest mismatch.");
            }
            // Check credential again after external IO; do not issue receipt for revoked session.
            var active=await kiosks.GetSessionAsync(secret,ct);
            if(active.Data?.KioskId!=kioskId)throw AppException.Unauthorized("Kiosk credential changed.");
            var asset=new MediaAsset{Category="image",ContentType="image/jpeg",FileName=name,SizeBytes=bytes.LongLength,
                Provider=storage.ProviderName,StorageKey=uploaded.StorageKey,IsShared=false,DeviceId=profile.CameraDeviceId,
                RelatedEntityType="device",RelatedEntityId=profile.CameraDeviceId,
                Notes=WaterBenchResultPolicy.Marker(station.Id,request.BootId,request.BatchId,request.Analyte,request.CaptureId)};
            var receipt=new StoredReceipt(asset.Id,request.Sha256,"water-bench-media-v1",profile.Id,profile.CameraDeviceId,
                station.ConfigurationVersion,requestHash,request.Width,request.Height,request.Crc32,request.Sequence,request.RequestedAt,request.ReceivedAt);
            string payload=JsonSerializer.Serialize(receipt,WaterBatchPlanning.Json);
            var row=new WaterBenchEvidence{StationId=station.Id,EventId=request.EventId,ConnectionId=request.ConnectionId,Kind="jpeg",
                BootId=request.BootId,FirmwareBatchId=request.BatchId,FirmwareSessionId=request.SessionId,Analyte=request.Analyte,
                CaptureId=request.CaptureId,KioskReceivedAt=request.ReceivedAt.UtcDateTime,PayloadJson=payload,
                PayloadSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()};
            db.MediaAssets.Add(asset);db.WaterBenchEvidence.Add(row);
            await db.SaveChangesAsync(ct);
            commitAttempted=true;await tx.CommitAsync(ct);
            return Ok(ApiResponse<object>.Ok(new{eventId=row.EventId,receiptId=row.Id,mediaId=asset.Id,sha256=request.Sha256,duplicate=false}));
        }
        catch
        {
            // Do not delete on ambiguous transaction commit. Reconcile orphan objects separately.
            if(uploaded!=null&&!commitAttempted)
            {
                try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(15));await storage.DeleteAsync(uploaded.StorageKey,cleanup.Token);}
                catch(Exception ex){log.LogWarning("WaterBench orphan cleanup failed for event {EventId}: {Type}",request.EventId,ex.GetType().Name);}
            }
            throw;
        }
    }
}
