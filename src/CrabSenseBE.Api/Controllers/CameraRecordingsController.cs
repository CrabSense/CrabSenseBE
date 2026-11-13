using System.Security.Claims;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.SystemPlatform;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/cameras/recordings")]
public sealed class CameraRecordingsController(CameraRecordingLibrary library, ISystemPlatformService platform) : ControllerBase
{
    // Preserve legacy array response for existing clients. Any explicit paging/filter opts into the richer envelope.
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? deviceId, [FromQuery] Guid? farmingAreaId,
        [FromQuery] Guid? farmingRowId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        if (page is null && pageSize is null && farmingAreaId is null && farmingRowId is null && from is null && to is null && search is null)
            return Ok(await platform.GetRecordingHistoryAsync(deviceId, ct));
        return Ok(ApiResponse<RecordingLibraryPage>.Ok(await library.ListAsync(
            new RecordingLibraryQuery(deviceId, farmingAreaId, farmingRowId, from, to, search, page ?? 1, pageSize ?? 25), ct)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RecordingLibraryItem>.Ok(await library.DetailAsync(id, ct)));

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var file = await library.OpenAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var file = await library.OpenAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool confirm, CancellationToken ct)
    {
        if (!confirm) throw AppException.BadRequest("Cần confirm=true để xóa video.");
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var actor))
            throw AppException.Unauthorized();
        await library.DeleteAsync(id, actor, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return Ok(ApiResponse.Ok("Đã xóa video và metadata."));
    }
}
