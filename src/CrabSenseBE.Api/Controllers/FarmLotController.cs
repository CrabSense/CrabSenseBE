using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Route("api/crab-lots")]
[Authorize]
[Tags("05. CRUD — Crab Lots")]
[Produces("application/json")]
public class CrabLotsController : ControllerBase
{
    private readonly IFarmLotService _service;
    public CrabLotsController(IFarmLotService service) => _service = service;

    /// <summary>[READ] List all crab lots</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetLotsAsync(ct));

    /// <summary>[READ] Next LOT-yyyyMMdd-001</summary>
    [HttpGet("next-code")]
    public async Task<IActionResult> NextCode([FromQuery] DateTime? importDate, CancellationToken ct)
        => Ok(await _service.GetNextLotCodeAsync(importDate, ct));

    /// <summary>[READ] Get crab lot by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetLotByIdAsync(id, ct));

    /// <summary>[CREATE] Create crab lot</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateCrabLotRequest req, CancellationToken ct)
        => Ok(await _service.CreateLotAsync(req, ct));

    /// <summary>[UPDATE] Update crab lot</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCrabLotRequest req, CancellationToken ct)
        => Ok(await _service.UpdateLotAsync(id, req, ct));

    /// <summary>[CREATE] Upload ảnh lô nhập lên Google Drive — CrabLots/{mã lô}/. URL ghi vào ImageUrlsJson.</summary>
    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(30_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImages(
        Guid id,
        [FromForm] List<IFormFile>? files,
        IFormFile? file,
        CancellationToken ct = default)
        => Ok(await _service.UploadImagesAsync(id, ToImageFiles(files, file), TryGetUserId(), ct));

    /// <summary>[READ] Stream 1 ảnh lô (Drive). Desktop dùng kèm Bearer.</summary>
    [HttpGet("{id:guid}/photos/{index:int}")]
    public async Task<IActionResult> GetPhoto(Guid id, int index, CancellationToken ct)
    {
        var photo = await _service.GetPhotoAsync(id, index, ct);
        if (photo is null) return NotFound();
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>[DELETE] Delete crab lot (no crabs referencing it)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteLotAsync(id, ct));

    private static IReadOnlyList<CrabImageFile> ToImageFiles(List<IFormFile>? files, IFormFile? file)
    {
        var list = new List<IFormFile>();
        if (files is { Count: > 0 })
            list.AddRange(files.Where(f => f is { Length: > 0 }));
        if (file is { Length: > 0 } && list.All(f => f != file))
            list.Add(file);
        return list
            .Select(f => new CrabImageFile(
                f.OpenReadStream(), f.FileName, f.ContentType ?? "application/octet-stream"))
            .ToList();
    }

    private Guid? TryGetUserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return raw is not null && Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }
}

[ApiController]
[Route("api/crop-batches")]
[Authorize]
[Tags("06. CRUD — Crop Batches")]
[Produces("application/json")]
public class CropBatchesController : ControllerBase
{
    private readonly IFarmLotService _service;
    public CropBatchesController(IFarmLotService service) => _service = service;

    /// <summary>[READ] List all crop batches</summary>
    // [HttpGet]
    // public async Task<IActionResult> GetAll(CancellationToken ct)
    //     => Ok(await _service.GetBatchesAsync(ct));

    /// <summary>[CREATE] Create crop batch</summary>
    // [HttpPost]
    // [Authorize(Roles = AppRoles.FarmWrite)]
    // public async Task<IActionResult> Create([FromBody] CreateCropBatchRequest req, CancellationToken ct)
    //     => Ok(await _service.CreateBatchAsync(req, ct));

    /// <summary>[UPDATE] Update crop batch</summary>
//     [HttpPut("{id:guid}")]
//     [Authorize(Roles = AppRoles.FarmWrite)]
//     public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCropBatchRequest req, CancellationToken ct)
//         => Ok(await _service.UpdateBatchAsync(id, req, ct));

//     /// <summary>[DELETE] Delete crop batch / vụ nuôi (no crabs referencing it)</summary>
//     [HttpDelete("{id:guid}")]
//     [Authorize(Roles = AppRoles.FarmWrite)]
//     public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
//         => Ok(await _service.DeleteBatchAsync(id, ct));
}
