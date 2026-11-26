using CrabSenseBE.Api.Services;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/areas/{areaId:guid}/water-bench/batches/{stationId:guid}/{bootId}/{batchId}/receipts")]
[Authorize(Roles=AppRoles.FarmWrite)]
public sealed class WaterBenchReceiptController(AppDbContext db,IUnitOfWork uow,ICurrentUserService user):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid areaId,Guid stationId,string bootId,string batchId,CancellationToken ct)
    {
        await new FarmAreaScope(uow,user).EnsureAsync(areaId,false,ct);
        if(stationId==Guid.Empty||!WaterBenchBatchTimeline.Hex16(bootId)||!WaterBenchBatchTimeline.Hex16(batchId))
            throw AppException.BadRequest("Invalid batch identity.");
        Response.Headers.CacheControl="no-store";
        var scoped=from e in db.WaterBenchEvidence.AsNoTracking()
                   join s in db.WaterBenchStations on e.StationId equals s.Id
                   join k in db.FarmKiosks on s.KioskId equals k.Id
                   where k.FarmingAreaId==areaId&&e.StationId==stationId&&e.BootId==bootId&&e.FirmwareBatchId==batchId
                   select e;
        var rows=await scoped.Where(x=>x.Kind=="batch_start"||x.Kind=="session"||x.Kind=="batch_status"||
            x.Kind=="image_request"||x.Kind=="operator_event"||x.Kind=="jpeg"||x.Kind=="sample_evidence"||x.Kind=="nh3_image_link")
            .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).Take(518).ToListAsync(ct);
        if(!rows.Any(x=>x.Kind=="batch_start"))throw AppException.NotFound("Water bench batch");
        if(rows.Count>517)throw AppException.Conflict("Receipt inventory exceeds batch capacity.");
        var items=rows.Select(WaterBenchReceiptPolicy.Read).ToArray();
        var timeline=WaterBenchBatchTimeline.Replay(rows.Where(x=>x.Kind is "batch_start" or "session" or "batch_status" or "image_request" or "operator_event").Select(x=>x.PayloadJson));
        if(items.Any(x=>x.ConnectionId!=timeline.ConnectionId)||
            items.Where(x=>x.MediaId.HasValue).Select(x=>x.MediaId).Distinct().Count()!=items.Count(x=>x.MediaId.HasValue)||
            items.Count(x=>x.Kind=="nh3")>2||items.Count(x=>x.Kind=="media")>3)
            throw AppException.Conflict("Ambiguous receipt inventory.");
        var mediaIds=items.Where(x=>x.MediaId.HasValue).Select(x=>x.MediaId!.Value).ToArray();
        var assets=await db.MediaAssets.AsNoTracking().Where(x=>mediaIds.Contains(x.Id)).ToListAsync(ct);
        var response=items.OrderBy(x=>x.Sequence??int.MaxValue).ThenBy(x=>x.RecordedAt).ThenBy(x=>x.EventId)
            .Select(x=>new{x.EventId,x.ReceiptId,x.Kind,x.EvidenceKind,x.ConnectionId,x.SessionId,x.CaptureId,x.Analyte,x.Sequence,
                x.KioskReceivedAt,x.RecordedAt,x.ServerPayloadSha256,x.MediaId,x.Sha256,
                privateMediaMetadataPresent=x.Kind!="media"?(bool?)null:assets.Any(a=>a.Id==x.MediaId&&a.Provider=="GoogleDrive"&&!a.IsShared&&
                    a.ContentType=="image/jpeg"&&a.Notes==WaterBenchResultPolicy.Marker(stationId,bootId,batchId,x.Analyte!,x.CaptureId!.Value))}).ToArray();
        return Ok(ApiResponse<object>.Ok(new{stationId,bootId,batchId,inventoryComplete=true,total=response.Length,
            timelineCount=items.Count(x=>x.Kind=="batch"),mediaCount=items.Count(x=>x.Kind=="media"),nh3Count=items.Count(x=>x.Kind=="nh3"),
            timelineStatus=timeline.Status,receiptBasis="persisted_backend_evidence",driveContentRechecked=false,
            physicalCompletionVerified=false,chemicalResultValidated=false,items=response}));
    }
}
