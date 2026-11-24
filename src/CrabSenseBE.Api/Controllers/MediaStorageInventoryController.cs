using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

/// <summary>Technical storage inventory; contains aggregate metadata only.</summary>
[ApiController]
[Route("api/system/media-storage")]
[Authorize(Roles=AppRoles.Platform)]
public sealed class MediaStorageInventoryController(AppDbContext db):ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var groups=await db.MediaAssets.AsNoTracking()
            .GroupBy(x=>new{x.Provider,x.Category,x.IsShared})
            .Select(g=>new{g.Key.Provider,g.Key.Category,g.Key.IsShared,
                FileCount=g.Count(),Bytes=g.Sum(x=>x.SizeBytes)})
            .ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new{
            source="database_metadata", measuredAtUtc=DateTime.UtcNow,
            totalFiles=groups.Sum(x=>(long)x.FileCount),
            totalBytes=groups.Sum(x=>x.Bytes),
            groups=groups.OrderBy(x=>x.Provider).ThenBy(x=>x.Category).ThenBy(x=>x.IsShared)
        }));
    }
}
