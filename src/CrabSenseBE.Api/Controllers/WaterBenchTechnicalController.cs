using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

/// <summary>Platform-only technical metadata. Never exposes farm assay data or credentials.</summary>
[ApiController]
[Route("api/system/water-bench")]
[Authorize(Roles=AppRoles.Platform)]
public sealed class WaterBenchTechnicalController(AppDbContext db):ControllerBase
{
    private static readonly string[] TechnicalKinds =
        ["inventory","connection_open","connection_closed","telemetry_observation","camera_error","clock_observation"];

    [HttpGet("stations")]
    public async Task<IActionResult> Stations([FromQuery] int page=1,[FromQuery] int pageSize=25,CancellationToken ct=default)
    {
        if(page is <1 or >100000 || pageSize is <1 or >50)
            throw AppException.BadRequest("Invalid WaterBench technical page.");
        Response.Headers["Cache-Control"]="no-store";
        var now=DateTime.UtcNow;
        var since=now.AddHours(-24);
        var query=from s in db.WaterBenchStations.AsNoTracking()
                  join k in db.FarmKiosks.AsNoTracking() on s.KioskId equals k.Id
                  select new { id=s.Id,s.KioskId,k.Code,k.Status,k.LastSeenAt,k.SoftwareVersion,
                    s.MasterHardwareId,s.SlaveHardwareId,s.ConfigurationVersion,s.RevokedAt };
        var total=await query.CountAsync(ct);
        var stations=await query.OrderBy(x=>x.RevokedAt.HasValue).ThenBy(x=>x.Code).ThenBy(x=>x.id)
            .Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        var ids=stations.Select(x=>x.id).ToArray();

        // Only SQL-side projections of known technical kinds; never deserialize raw evidence.
        var evidence=await db.WaterBenchEvidence.AsNoTracking()
            .Where(e=>ids.Contains(e.StationId)&&TechnicalKinds.Contains(e.Kind))
            .GroupBy(e=>e.StationId).Select(g=>new{
                stationId=g.Key,lastTechnicalReceiptAt=g.Max(e=>e.CreatedAt),
                technicalReceipts24h=g.Count(e=>e.CreatedAt>=since),
                cameraErrors24h=g.Count(e=>e.CreatedAt>=since&&e.Kind=="camera_error"),
                clockObservations24h=g.Count(e=>e.CreatedAt>=since&&e.Kind=="clock_observation")
            }).ToListAsync(ct);
        var attempts=await (from a in db.WaterBenchAiAttempts.AsNoTracking()
                            join e in db.WaterBenchEvidence.AsNoTracking() on a.SessionEvidenceId equals e.Id
                            where ids.Contains(e.StationId)&&a.CreatedAt>=since
                            select new {e.StationId,a.State,a.LeaseUntil})
            .GroupBy(x=>x.StationId).Select(g=>new{
                stationId=g.Key,
                retryableFailures24h=g.Count(x=>x.State=="retryable"),
                contractBlocked24h=g.Count(x=>x.State=="contract_blocked"),
                expiredRunningLeases24h=g.Count(x=>x.State=="running"&&x.LeaseUntil<=now),
                activeRunningLeases24h=g.Count(x=>x.State=="running"&&x.LeaseUntil>now)
            }).ToListAsync(ct);
        var byEvidence=evidence.ToDictionary(x=>x.stationId);
        var byAttempts=attempts.ToDictionary(x=>x.stationId);
        var items=stations.Select(s=>{
            var e=byEvidence.GetValueOrDefault(s.id);
            var a=byAttempts.GetValueOrDefault(s.id);
            return new{
                s.id,s.KioskId,kioskCode=s.Code,kioskReportedStatus=s.Status,
                kioskLastSeenAt=s.LastSeenAt,kioskSoftwareVersion=s.SoftwareVersion,
                s.MasterHardwareId,s.SlaveHardwareId,s.ConfigurationVersion,stationRevoked=s.RevokedAt!=null,
                lastTechnicalReceiptAt=e?.lastTechnicalReceiptAt,
                technicalReceiptFreshness=e==null?"unobserved":now-e.lastTechnicalReceiptAt>TimeSpan.FromMinutes(2)?"stale":"recent",
                technicalReceipts24h=e?.technicalReceipts24h??0,
                cameraErrors24h=e?.cameraErrors24h??0,
                clockObservations24h=e?.clockObservations24h??0,
                retryableFailures24h=a?.retryableFailures24h??0,
                contractBlocked24h=a?.contractBlocked24h??0,
                expiredRunningLeases24h=a?.expiredRunningLeases24h??0,
                activeRunningLeases24h=a?.activeRunningLeases24h??0
            };
        }).ToArray();
        return Ok(ApiResponse<object>.Ok(new{
            generatedAt=now,windowStart=since,windowHours=24,page,pageSize,total,items,
            source="persisted_technical_metadata",liveHardwareVerified=false,
            attemptCountsAreHistorical=true,chemicalDataIncluded=false,commandsEnabled=false
        }));
    }
}
