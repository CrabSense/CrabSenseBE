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
