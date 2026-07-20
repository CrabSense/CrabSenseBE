using CrabSenseBE.Application.DTOs.Media;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/media")]
[Authorize]
[Tags("12. Media (Drive)")]
[Produces("application/json")]
public class MediaController : ControllerBase
{
    private readonly IMediaService _service;
    public MediaController(IMediaService service) => _service = service;

    /// <summary>[CREATE] Upload image / video / log to Google Drive</summary>
    [HttpPost("upload")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(524_288_000)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] string category = "image",
        [FromForm] Guid? boxId = null,
        [FromForm] Guid? crabId = null,
        [FromForm] Guid? deviceId = null,
        [FromForm] string? relatedEntityType = null,
        [FromForm] Guid? relatedEntityId = null,
        [FromForm] string? notes = null,
        [FromForm] bool sharePublic = true,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { success = false, message = "File is required." });

        await using var stream = file.OpenReadStream();
        var meta = new MediaUploadMeta(
            category, boxId, crabId, deviceId, relatedEntityType, relatedEntityId, notes, sharePublic);

        Guid? userId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
        if (Guid.TryParse(claim, out var parsed))
            userId = parsed;

        return Ok(await _service.UploadAsync(stream, file.FileName, file.ContentType, meta, userId, ct));
    }

    /// <summary>[READ] List media — no filter = GET ALL</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? category = null,
        [FromQuery] Guid? boxId = null,
        [FromQuery] Guid? crabId = null,
        CancellationToken ct = default)
        => Ok(await _service.ListAsync(category, boxId, crabId, ct));

    /// <summary>[READ] Get media file by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>[UPDATE] Create / refresh Drive share link</summary>
    [HttpPost("{id:guid}/share")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Share(Guid id, CancellationToken ct)
        => Ok(await _service.ShareAsync(id, ct));

    /// <summary>[DELETE] Delete media</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteAsync(id, ct));
}
