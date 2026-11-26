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
[Route("api/edge/water-bench")]
public sealed class WaterBenchNh3EdgeController(AppDbContext db,IKioskProvisioningService kiosks):ControllerBase
{
    [HttpPost("nh3-evidence")]
    [AllowAnonymous]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Receive(Nh3EvidenceEvent request,CancellationToken ct)
    {
        var session=await kiosks.GetSessionAsync(Request.Headers["X-Kiosk-Key"].FirstOrDefault()??"",ct);
        var kioskId=session.Data?.KioskId??throw AppException.Unauthorized("Kiosk credential required.");
        WaterBenchNh3Policy.Validate(request);
        var kind=request.Type=="sample"?"sample_evidence":"nh3_image_link";
        string payload=JsonSerializer.Serialize(request,WaterBatchPlanning.Json);
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchStations\" WHERE \"Id\"={request.StationId} AND \"KioskId\"={kioskId} FOR UPDATE",ct);
        var station=await db.WaterBenchStations.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==request.StationId&&s.KioskId==kioskId&&s.RevokedAt==null,ct)
            ??throw AppException.NotFound("Active water bench station");
        if(station.MasterHardwareId!=request.MasterHardwareId||station.SlaveHardwareId!=request.SlaveHardwareId)
            throw AppException.Conflict("Hardware identity mismatch.");
        if(!await db.FarmKiosks.AnyAsync(k=>k.Id==kioskId&&k.Status!="Revoked",ct))throw AppException.Unauthorized("Kiosk revoked.");
        var events=db.WaterBenchEvidence.AsNoTracking().Where(x=>x.StationId==station.Id);
        var prior=await events.SingleOrDefaultAsync(x=>x.EventId==request.EventId,ct);
        if(prior!=null)
        {
            if(prior.Kind!=kind||prior.PayloadSha256!=hash||prior.PayloadJson!=payload)
                throw AppException.Conflict("Event ID has different content.");
            return Ok(ApiResponse<object>.Ok(new{eventId=prior.EventId,receiptId=prior.Id,duplicate=true}));
        }
        var batch=events.Where(x=>x.BootId==request.BootId&&x.FirmwareBatchId==request.BatchId);
        // Only typed timeline kinds are replayed. NH3 evidence is separate.
        var rows=await batch.Where(x=>x.Kind=="batch_start"||x.Kind=="session"||x.Kind=="batch_status"||
            x.Kind=="image_request"||x.Kind=="operator_event").Select(x=>x.PayloadJson).Take(WaterBenchBatchTimeline.MaxEvents+1).ToListAsync(ct);
        if(rows.Count==0||rows.Count>WaterBenchBatchTimeline.MaxEvents)throw AppException.Conflict("Batch timeline unavailable.");
        var timeline=WaterBenchBatchTimeline.Replay(rows);
        var typed=rows.Select(x=>JsonSerializer.Deserialize<BenchBatchEvent>(x,WaterBatchPlanning.Json)!).ToArray();
        var sessionEvent=typed.SingleOrDefault(x=>x.Type=="session"&&x.Analyte=="NH3"&&x.SessionId==request.SessionId)
            ??throw AppException.Conflict("NH3 session observation required.");
        var image=typed.SingleOrDefault(x=>x.Type=="image"&&x.Analyte=="NH3"&&x.SessionId==request.SessionId);
        WaterBenchNh3Policy.Match(request,timeline,sessionEvent,image);
        if(await batch.AnyAsync(x=>x.Kind==kind&&x.ConnectionId==request.ConnectionId&&x.FirmwareSessionId==request.SessionId&&x.Analyte=="NH3",ct))
            throw AppException.Conflict("NH3 evidence already exists; retry original event ID.");
        var row=new WaterBenchEvidence{StationId=station.Id,EventId=request.EventId,ConnectionId=request.ConnectionId,
            BootId=request.BootId,FirmwareBatchId=request.BatchId,FirmwareSessionId=request.SessionId,Analyte="NH3",
            CaptureId=request.Image?.CaptureId,Kind=kind,KioskReceivedAt=request.ReceivedAt.UtcDateTime,PayloadJson=payload,PayloadSha256=hash};
        db.WaterBenchEvidence.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{eventId=row.EventId,receiptId=row.Id,duplicate=false}));
    }
}

[ApiController]
[Route("api/areas/{areaId:guid}/water-bench/batches/{stationId:guid}/{bootId}/{batchId}/nh3-evidence")]
[Authorize(Roles=AppRoles.FarmWrite)]
public sealed class WaterBenchNh3Controller(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await new FarmAreaScope(uow,user).EnsureAsync(areaId,false,ct);
        if(!WaterBenchBatchTimeline.Hex16(bootId)||!WaterBenchBatchTimeline.Hex16(batchId))throw AppException.BadRequest("Invalid batch identity.");
        var scoped=from e in db.WaterBenchEvidence.AsNoTracking()
                   join s in db.WaterBenchStations on e.StationId equals s.Id
                   join k in db.FarmKiosks on s.KioskId equals k.Id
                   where k.FarmingAreaId==areaId&&e.StationId==stationId&&e.BootId==bootId&&e.FirmwareBatchId==batchId
                   select e;
        if(!await scoped.AnyAsync(e=>e.Kind=="batch_start",ct))throw AppException.NotFound("Water bench batch");
        var rows=await scoped.Where(e=>e.Kind=="sample_evidence"||e.Kind=="nh3_image_link").OrderBy(e=>e.CreatedAt).Take(3).ToListAsync(ct);
        if(rows.Count>2)throw AppException.Conflict("Ambiguous NH3 evidence.");
        var data=rows.Select(x=>new{receiptId=x.Id,kind=x.Kind,evidence=JsonSerializer.Deserialize<Nh3EvidenceEvent>(x.PayloadJson,WaterBatchPlanning.Json)}).ToArray();
        return Ok(ApiResponse<object>.Ok(new{observations=data,sampleReceived=rows.Any(x=>x.Kind=="sample_evidence"),
            imageLinkReceived=rows.Any(x=>x.Kind=="nh3_image_link"),clockBasis="master_boot_uptime",
            temperatureBasis="modbus_register_observation_not_conversion",salinityPpt=(decimal?)null,
            sensorVialAlignmentVerified=false,clockDriftValidated=false,actualExposureVerified=false,chemicalResultValidated=false}));
    }
}
