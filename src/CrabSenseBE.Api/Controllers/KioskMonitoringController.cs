using CrabSenseBE.Application.Common;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/kiosks/monitoring")]
public sealed class KioskMonitoringController(AppDbContext db):ControllerBase
{
    public record Metrics(double? ProcessCpuPercent,long? ProcessMemoryBytes,long? DiskTotalBytes,long? DiskFreeBytes,string? SoftwareVersion,
        DateTimeOffset? EdgeSyncedAt,DateTimeOffset? RecordingCheckedAt,DateTimeOffset? UploadCheckedAt);
    internal static bool Valid(Metrics m,DateTime now)
    {
        bool Time(DateTimeOffset? value)=>value==null||value.Value.UtcDateTime<=now.AddSeconds(5)&&value.Value.UtcDateTime>=now.AddDays(-30);
        return (m.ProcessCpuPercent==null||double.IsFinite(m.ProcessCpuPercent.Value)&&m.ProcessCpuPercent is >=0 and <=100)
            &&(m.ProcessMemoryBytes==null||m.ProcessMemoryBytes>=0)&&(m.DiskTotalBytes==null||m.DiskTotalBytes>=0)
            &&(m.DiskFreeBytes==null||m.DiskFreeBytes>=0&&m.DiskTotalBytes.HasValue&&m.DiskFreeBytes<=m.DiskTotalBytes)
            &&(m.SoftwareVersion?.Length??0)<=80&&Time(m.EdgeSyncedAt)&&Time(m.RecordingCheckedAt)&&Time(m.UploadCheckedAt);
    }
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Report(Metrics m,CancellationToken ct)
    {
        var kiosk=await CameraRecordingKioskAuth.Require(db,Request,ct);var now=DateTime.UtcNow;
        if(!Valid(m,now))throw AppException.BadRequest("Invalid kiosk monitoring metrics.");
        var row=await db.FarmKiosks.SingleOrDefaultAsync(x=>x.Id==kiosk.Id&&x.Status!="Revoked",ct)??throw AppException.Unauthorized();
        row.MetricsReportedAt=now;row.ProcessCpuPercent=m.ProcessCpuPercent;row.ProcessMemoryBytes=m.ProcessMemoryBytes;
        row.DiskTotalBytes=m.DiskTotalBytes;row.DiskFreeBytes=m.DiskFreeBytes;row.SoftwareVersion=m.SoftwareVersion;
        row.EdgeSyncedAt=m.EdgeSyncedAt?.UtcDateTime;row.RecordingCheckedAt=m.RecordingCheckedAt?.UtcDateTime;row.UploadCheckedAt=m.UploadCheckedAt?.UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{kioskId=row.Id,reportedAt=now}));
    }
}
