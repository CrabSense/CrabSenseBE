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
public sealed class WaterBenchBatchEdgeController(AppDbContext db,IKioskProvisioningService kiosks):ControllerBase
{
    [AllowAnonymous]
    [HttpPost("batch-events")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Receive(BenchBatchEvent request,CancellationToken ct)
    {
        var session=await kiosks.GetSessionAsync(Request.Headers["X-Kiosk-Key"].FirstOrDefault()??"",ct);
        var kioskId=session.Data?.KioskId??throw AppException.Unauthorized("Kiosk credential required.");
        WaterBenchBatchTimeline.ValidateEnvelope(request);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Serialize all ingestion per station, including concurrent retries.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchStations\" WHERE \"Id\"={request.StationId} AND \"KioskId\"={kioskId} FOR UPDATE",ct);
        var station=await db.WaterBenchStations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.StationId&&x.KioskId==kioskId&&x.RevokedAt==null,ct)
            ??throw AppException.NotFound("Active water bench station");
        if(!await db.FarmKiosks.AnyAsync(x=>x.Id==kioskId&&x.Status!="Revoked",ct))
            throw AppException.Unauthorized("Kiosk revoked.");
        // Hash canonical typed representation; not a hash of HTTP transport bytes.
        string payload=JsonSerializer.Serialize(request,WaterBatchPlanning.Json);
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var prior=await db.WaterBenchEvidence.AsNoTracking().SingleOrDefaultAsync(x=>x.StationId==station.Id&&x.EventId==request.EventId,ct);
        if(prior!=null)
        {
            if(prior.PayloadSha256!=hash||prior.PayloadJson!=payload)throw AppException.Conflict("Event ID has different content.");
            return Ok(ApiResponse<object>.Ok(new{eventId=prior.EventId,receiptId=prior.Id,duplicate=true}));
        }
        var rows=await db.WaterBenchEvidence.AsNoTracking().Where(x=>x.StationId==station.Id&&x.BootId==request.BootId&&
            x.FirmwareBatchId==request.BatchId&&(x.Kind=="batch_start"||x.Kind=="session"||x.Kind=="batch_status"||x.Kind=="image_request"||x.Kind=="operator_event"))
            .Select(x=>x.PayloadJson).Take(WaterBenchBatchTimeline.MaxEvents+1).ToListAsync(ct);
        if(rows.Count>=WaterBenchBatchTimeline.MaxEvents)throw AppException.Conflict("Batch observation capacity reached.");
        var timeline=WaterBenchBatchTimeline.Replay(rows);
        timeline.Apply(request);
        var row=new WaterBenchEvidence{StationId=station.Id,EventId=request.EventId,ConnectionId=request.ConnectionId,
            BootId=request.BootId,FirmwareBatchId=request.BatchId,FirmwareSessionId=request.SessionId,Kind=WaterBenchBatchTimeline.EvidenceKind(request.Type),
            RequestId=request.RequestId,CaptureId=request.CaptureId,Analyte=request.Analyte,KioskReceivedAt=request.ReceivedAt.UtcDateTime,
            PayloadJson=payload,PayloadSha256=hash};
        db.WaterBenchEvidence.Add(row);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{eventId=row.EventId,receiptId=row.Id,duplicate=false,status=timeline.Status}));
    }
    [AllowAnonymous]
    [HttpPost("audit-events")]
    [RequestSizeLimit(32768)]
    public async Task<IActionResult> ReceiveAudit(WaterBenchAuditEvent request,CancellationToken ct)
    {
        var session=await kiosks.GetSessionAsync(Request.Headers["X-Kiosk-Key"].FirstOrDefault()??"",ct);
        var kioskId=session.Data?.KioskId??throw AppException.Unauthorized("Kiosk credential required.");
        WaterBenchAuditPolicy.Validate(request);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM be.\"WaterBenchStations\" WHERE \"Id\"={request.StationId} AND \"KioskId\"={kioskId} FOR UPDATE",ct);
        var station=await db.WaterBenchStations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.StationId&&x.KioskId==kioskId&&x.RevokedAt==null,ct)
            ??throw AppException.NotFound("Active water bench station");
        if(!await db.FarmKiosks.AnyAsync(x=>x.Id==kioskId&&x.Status!="Revoked",ct))
            throw AppException.Unauthorized("Kiosk revoked.");
        string payload=JsonSerializer.Serialize(request,WaterBatchPlanning.Json);
        string hash=WaterBenchAuditPolicy.Hash(payload);
        var prior=await db.WaterBenchEvidence.AsNoTracking().SingleOrDefaultAsync(x=>x.StationId==station.Id&&x.EventId==request.EventId,ct);
        if(prior!=null)
        {
            if(prior.Kind!=request.Kind||prior.PayloadSha256!=hash||prior.PayloadJson!=payload)throw AppException.Conflict("Event ID has different content.");
            return Ok(ApiResponse<object>.Ok(new{eventId=prior.EventId,receiptId=prior.Id,duplicate=true}));
        }
        if(request.Kind=="command_outcome")
        {
            var attemptId=WaterBenchAuditPolicy.EventId(station.Id,"command",request.JournalId,0);
            var attempt=await db.WaterBenchEvidence.AsNoTracking().SingleOrDefaultAsync(x=>x.StationId==station.Id&&x.EventId==attemptId&&x.Kind=="command",ct)
                ??throw AppException.Conflict("Command attempt required.");
            WaterBenchAuditPolicy.ValidateOutcome(request,JsonSerializer.Deserialize<WaterBenchAuditEvent>(attempt.PayloadJson,WaterBatchPlanning.Json)!);
        }
        if(request.Kind=="clean_status"&&request.Sequence>1)
        {
            var previousId=WaterBenchAuditPolicy.EventId(station.Id,request.Kind,request.JournalId,request.Sequence-1);
            var previous=await db.WaterBenchEvidence.AsNoTracking().SingleOrDefaultAsync(x=>x.StationId==station.Id&&x.EventId==previousId&&x.Kind=="clean_status",ct)
                ??throw AppException.Conflict("Previous cleaning event required.");
            WaterBenchAuditPolicy.ValidatePrevious(request,JsonSerializer.Deserialize<WaterBenchAuditEvent>(previous.PayloadJson,WaterBatchPlanning.Json)!);
        }
        var row=new WaterBenchEvidence{StationId=station.Id,EventId=request.EventId,ConnectionId=request.ConnectionId,
            BootId=request.BootId,Kind=request.Kind,RequestId=request.JournalId,KioskReceivedAt=request.At.UtcDateTime,
            PayloadJson=payload,PayloadSha256=hash};
        db.WaterBenchEvidence.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{eventId=row.EventId,receiptId=row.Id,duplicate=false}));
    }

}

[ApiController]
[Route("api/areas/{areaId:guid}/water-bench/batches")]
[Authorize(Roles=AppRoles.FarmWrite)]
public sealed class WaterBenchBatchController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    private readonly FarmAreaScope scope=new(uow,user);
    private IQueryable<WaterBenchEvidence> Scoped(Guid areaId)=>
        from e in db.WaterBenchEvidence.AsNoTracking()
        join s in db.WaterBenchStations on e.StationId equals s.Id
        join k in db.FarmKiosks on s.KioskId equals k.Id
        where k.FarmingAreaId==areaId
        select e;
    [HttpGet]
    public async Task<IActionResult> List(Guid areaId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        var starts=await Scoped(areaId).Where(e=>e.Kind=="batch_start").OrderByDescending(e=>e.CreatedAt).Take(50)
            .Select(e=>new{e.StationId,e.BootId,e.FirmwareBatchId,e.ConnectionId,e.CreatedAt,e.PayloadJson}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(starts.Select(e=>new{e.StationId,e.BootId,e.FirmwareBatchId,e.ConnectionId,e.CreatedAt,
            start=JsonSerializer.Deserialize<BenchBatchEvent>(e.PayloadJson,WaterBatchPlanning.Json)})));
    }

    [HttpGet("audit/{stationId:guid}")]
    public async Task<IActionResult> Audit(Guid areaId,Guid stationId,[FromQuery] int skip=0,[FromQuery] int take=25,CancellationToken ct=default)
    {
        await scope.EnsureAsync(areaId,false,ct);
        if(skip<0||take is <1 or >50)throw AppException.BadRequest("Invalid audit page.");
        if(!await (from s in db.WaterBenchStations join k in db.FarmKiosks on s.KioskId equals k.Id
            where s.Id==stationId&&k.FarmingAreaId==areaId select s.Id).AnyAsync(ct))
            throw AppException.NotFound("Water bench station");
        var query=Scoped(areaId).Where(e=>e.StationId==stationId&&(e.Kind=="command"||e.Kind=="command_outcome"||e.Kind=="clean_status"));
        var total=await query.CountAsync(ct);
        var rows=await query.OrderByDescending(e=>e.CreatedAt).ThenBy(e=>e.Id).Skip(skip).Take(take)
            .Select(e=>new{receiptId=e.Id,e.EventId,e.Kind,e.RequestId,e.CreatedAt,e.PayloadJson,e.PayloadSha256}).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{total,skip,take,items=rows.Select(e=>new{e.receiptId,e.EventId,e.Kind,e.RequestId,e.CreatedAt,
            e.PayloadSha256,observation=JsonSerializer.Deserialize<WaterBenchAuditEvent>(e.PayloadJson,WaterBatchPlanning.Json)}),
            physicalCompletionVerified=false,chemicalResidueMeasured=false,commandsReplayed=false}));
    }
    [HttpGet("{stationId:guid}/{bootId}/{batchId}")]
    public async Task<IActionResult> Detail(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await scope.EnsureAsync(areaId,false,ct);
        if(!WaterBenchBatchTimeline.Hex16(bootId)||!WaterBenchBatchTimeline.Hex16(batchId))
            throw AppException.BadRequest("Invalid batch identity.");
        var rows=await Scoped(areaId).Where(e=>e.StationId==stationId&&e.BootId==bootId&&e.FirmwareBatchId==batchId&&
            (e.Kind=="batch_start"||e.Kind=="session"||e.Kind=="batch_status"||e.Kind=="image_request"||e.Kind=="operator_event"))
            .Select(e=>e.PayloadJson).Take(WaterBenchBatchTimeline.MaxEvents+1).ToListAsync(ct);
        if(rows.Count==0)throw AppException.NotFound("Water bench batch");
        var timeline=WaterBenchBatchTimeline.Replay(rows);
        return Ok(ApiResponse<object>.Ok(new{timeline,observationStale=DateTimeOffset.UtcNow-timeline.LastObservedAt>TimeSpan.FromMinutes(2),
            clock="firmware_boot_uptime_and_kiosk_receipt",analyticalResultLinked=false}));
    }
}
