using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CrabSenseBE.Api.Controllers;

/// <summary>CRUD Farming Areas — Owner (chủ) comes from JWT on create</summary>
[ApiController]
[Route("api/farming-areas")]
[Authorize]
[Tags("01. CRUD — Farming Areas")]
[Produces("application/json")]
public class FarmingAreasController : ControllerBase
{
    private readonly IFarmingService _service;
    public FarmingAreasController(IFarmingService service) => _service = service;

    /// <summary>[READ] List / filter areas. FarmOwner is auto-scoped to JWT user when ownerId omitted.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? ownerId = null,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null,
        CancellationToken ct = default)
    {
        // Mobile Home passes ownerId from /auth/me; if missing, FarmOwner still only sees own areas.
        if (ownerId is null && User.IsInRole(AppRoles.FarmOwner))
            ownerId = TryGetUserId();

        return Ok(await _service.GetAreasAsync(
            new FarmingAreaFilter(search, isActive, status, page, pageSize, ownerId), ct));
    }

    /// <summary>[READ] Next khu code (AREA-A01…) from all areas in DB — not sent on create</summary>
    [HttpGet("next-code")]
    public async Task<IActionResult> GetNextCode(CancellationToken ct)
        => Ok(await _service.GetNextAreaCodeAsync(ct));

    /// <summary>[READ] Get area by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetAreaByIdAsync(id, ct));

    /// <summary>[CREATE] Create khu — body: name; code auto AREA-xxx; OwnerId from JWT</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateFarmingAreaRequest req, CancellationToken ct)
        => Ok(await _service.CreateAreaAsync(req, RequireUserId(), ct));

    /// <summary>[CREATE] Upload ảnh đại diện (chưa gắn khu). Gắn URL vào POST create (avatarUrl).</summary>
    [HttpPost("avatar")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(8_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken ct)
        => Ok(await _service.UploadAvatarAsync(null, RequireFile(file), file!.FileName,
            file.ContentType ?? "application/octet-stream", TryGetUserId(), ct));

    /// <summary>[CREATE] Upload và gắn ảnh đại diện cho khu đã có.</summary>
    [HttpPost("{id:guid}/avatar")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(8_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatarForArea(Guid id, IFormFile? file, CancellationToken ct)
        => Ok(await _service.UploadAvatarAsync(id, RequireFile(file), file!.FileName,
            file.ContentType ?? "application/octet-stream", TryGetUserId(), ct));

    /// <summary>[UPDATE] Update farming area</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFarmingAreaRequest req, CancellationToken ct)
        => Ok(await _service.UpdateAreaAsync(id, req, ct));

    /// <summary>
    /// [DELETE] Delete area. Mặc định chỉ xoá được khi khu đã hết hàng.
    /// cascade=true: xoá cả cây con (cua → hộp → hàng) trong 1 transaction.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] bool cascade = false, CancellationToken ct = default)
        => Ok(await _service.DeleteAreaAsync(id, cascade, ct));

    /// <summary>[READ] List rows in area — no pageSize = GET ALL</summary>
    [HttpGet("{areaId:guid}/rows")]
    public async Task<IActionResult> GetRows(
        Guid areaId,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null,
        CancellationToken ct = default)
        => Ok(await _service.GetRowsAsync(
            new FarmingRowFilter(areaId, search, isActive, null, page, pageSize), ct));

    /// <summary>[CREATE] Create row in area (path areaId = FarmingAreaId)</summary>
    [HttpPost("{areaId:guid}/rows")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateRow(Guid areaId, [FromBody] CreateFarmingRowRequest req, CancellationToken ct)
        => Ok(await _service.CreateRowAsync(req with { FarmingAreaId = areaId }, ct));

    private Guid RequireUserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        if (raw is null || !Guid.TryParse(raw, out var id) || id == Guid.Empty)
            throw AppException.Unauthorized("Missing user id claim in JWT.");
        return id;
    }

    private Guid? TryGetUserId()
    {
        var raw = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return raw is not null && Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private static Stream RequireFile(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
            throw AppException.BadRequest("Image file is required.");
        return file.OpenReadStream();
    }
}

/// <summary>CRUD Farming Rows</summary>
[ApiController]
[Route("api/farming-rows")]
[Authorize]
[Tags("02. CRUD — Farming Rows")]
[Produces("application/json")]
public class FarmingRowsController : ControllerBase
{
    private readonly IFarmingService _service;
    public FarmingRowsController(IFarmingService service) => _service = service;

    /// <summary>[READ] List / filter rows — no query = GET ALL</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] string? search = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null,
        CancellationToken ct = default)
        => Ok(await _service.GetRowsAsync(
            new FarmingRowFilter(farmingAreaId, search, isActive, status, page, pageSize), ct));

    /// <summary>[READ] Next dãy code (DAY-A01…) — not sent on create</summary>
    [HttpGet("next-code")]
    public async Task<IActionResult> GetNextCode(CancellationToken ct)
        => Ok(await _service.GetNextRowCodeAsync(ct));

    /// <summary>[READ] Get row by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetRowByIdAsync(id, ct));

    /// <summary>[CREATE] Create dãy — body: farmingAreaId + name; code auto DAY-xxx</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateFarmingRowRequest req, CancellationToken ct)
        => Ok(await _service.CreateRowAsync(req, ct));

    /// <summary>[UPDATE] Update farming row</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFarmingRowRequest req, CancellationToken ct)
        => Ok(await _service.UpdateRowAsync(id, req, ct));

    /// <summary>[DELETE] Delete row (only if no boxes remain)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteRowAsync(id, ct));

    /// <summary>[READ] List boxes in row — no pageSize = GET ALL</summary>
    [HttpGet("{rowId:guid}/boxes")]
    public async Task<IActionResult> GetBoxes(
        Guid rowId,
        [FromQuery] string? code = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? isOccupied = null,
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null,
        CancellationToken ct = default)
        => Ok(await _service.GetBoxesAsync(new BoxFilter(null, rowId, code, status, isOccupied, page, pageSize), ct));

    /// <summary>[CREATE] Create box in row — path rowId only; Area auto-filled from row</summary>
    [HttpPost("{rowId:guid}/boxes")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> CreateBox(Guid rowId, [FromBody] CreateBoxRequest? req, CancellationToken ct)
        => Ok(await _service.CreateBoxAsync(
            (req ?? new CreateBoxRequest(rowId)) with { FarmingRowId = rowId }, ct));
}

/// <summary>CRUD Crab Farm Boxes</summary>
[ApiController]
[Route("api/boxes")]
[Authorize]
[Tags("03. CRUD — Crab Farm Boxes")]
[Produces("application/json")]
public class BoxesController : ControllerBase
{
    private readonly IFarmingService _service;
    private readonly IBoxOverviewService _overview;
    private readonly IBoxDetailService _detail;

    public BoxesController(
        IFarmingService service,
        IBoxOverviewService overview,
        IBoxDetailService detail)
    {
        _service = service;
        _overview = overview;
        _detail = detail;
    }

    /// <summary>[READ] List / filter boxes — no query = GET ALL</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? farmingRowId = null,
        [FromQuery] string? code = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? isOccupied = null,
        [FromQuery] int page = 1,
        [FromQuery] int? pageSize = null,
        CancellationToken ct = default)
        => Ok(await _service.GetBoxesAsync(
            new BoxFilter(farmingAreaId, farmingRowId, code, status, isOccupied, page, pageSize), ct));

    /// <summary>
    /// [READ] Enriched Boxes tab overview — health score, water, devices, alerts, AI tips, map layout.
    /// </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? farmingRowId = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
        => Ok(await _overview.GetOverviewAsync(farmingAreaId, farmingRowId, status, ct));

    /// <summary>[READ] Empty boxes currently available (+ suggested next). Filter by area/row</summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable(
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] Guid? farmingRowId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetAvailabilityAsync(farmingAreaId, farmingRowId, ct));

    /// <summary>[READ] Mobile QR alias — returns { boxId } for scanner</summary>
    [HttpGet("qr/{code}")]
    public async Task<IActionResult> ResolveQr(string code, CancellationToken ct)
        => Ok(await _detail.ResolveQrAsync(code, ct));

    /// <summary>
    /// [READ] Mobile Scan QR Quick Result — single payload:
    /// box snapshot, health/AI scores, water, alerts, recommendation.
    /// </summary>
    [HttpGet("qr/{code}/quick-result")]
    public async Task<IActionResult> QuickResult(string code, CancellationToken ct)
        => Ok(await _detail.GetQuickResultByQrAsync(code, ct));

    /// <summary>[READ] Enriched box detail (Mobile Box Detail)</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _detail.GetDetailAsync(id, ct));

    /// <summary>[READ] Crabs currently in box</summary>
    [HttpGet("{id:guid}/crabs")]
    public async Task<IActionResult> GetCrabs(Guid id, CancellationToken ct)
        => Ok(await _detail.GetCrabsAsync(id, ct));

    /// <summary>[CREATE] Add crab into box (Mobile convenience; auto CrabLot if omitted)</summary>
    [HttpPost("{id:guid}/crabs")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> AddCrab(
        Guid id, [FromBody] CrabSenseBE.Application.DTOs.Ops.MobileAddCrabRequest req, CancellationToken ct)
        => Ok(await _detail.AddCrabAsync(id, req, ct));

    /// <summary>[READ] Videos for box (MediaAsset category=video)</summary>
    [HttpGet("{id:guid}/videos")]
    public async Task<IActionResult> GetVideos(Guid id, CancellationToken ct)
        => Ok(await _detail.GetVideosAsync(id, ct));

    /// <summary>[READ] Camera feed metadata for box (Mobile)</summary>
    [HttpGet("{id:guid}/camera")]
    public async Task<IActionResult> GetCamera(
        Guid id,
        [FromServices] IBoxCameraService cameras,
        CancellationToken ct)
        => Ok(await cameras.GetCameraForBoxAsync(id, ct));

    /// <summary>[CREATE] Create box — body: farmingRowId (dãy); khu auto from row; code optional</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateBoxRequest req, CancellationToken ct)
        => Ok(await _service.CreateBoxAsync(req, ct));

    /// <summary>[UPDATE] Update code / status / occupied</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBoxRequest req, CancellationToken ct)
        => Ok(await _service.UpdateBoxAsync(id, req, ct));

    /// <summary>[UPDATE] Update box farming status only</summary>
    [HttpPatch("{boxId:guid}/status")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> UpdateStatus(Guid boxId, [FromBody] UpdateBoxStatusRequest req, CancellationToken ct)
        => Ok(await _service.UpdateBoxStatusAsync(boxId, req, ct));

    /// <summary>[DELETE] Delete box (no live crabs)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteBoxAsync(id, ct));
}

/// <summary>CRUD Crabs</summary>
[ApiController]
[Route("api/crabs")]
[Authorize]
[Tags("04. CRUD — Crabs")]
[Produces("application/json")]
public class CrabsController : ControllerBase
{
    private readonly IFarmingService _service;
    private readonly ICrabImageService _images;
    public CrabsController(IFarmingService service, ICrabImageService images)
    {
        _service = service;
        _images = images;
    }

    /// <summary>[READ] List crabs — pageSize=0 or omit = GET ALL. farmingAreaId scopes to one khu/trại.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] Guid? farmingAreaId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetCrabsAsync(page, pageSize, farmingAreaId, ct));

    /// <summary>[READ] Next CRAB-0001… + QR-CRAB-0001</summary>
    [HttpGet("next-code")]
    public async Task<IActionResult> NextCode(CancellationToken ct)
        => Ok(await _service.GetNextCrabCodeAsync(ct));

    /// <summary>
    /// [CREATE] Upload crab photos to S3. Returns public URLs.
    /// Call this first, then POST /api/crabs with imageUrls. Or POST /api/crabs/{id}/images after create.
    /// Form field: files (multiple).
    /// </summary>
    [HttpPost("images")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(30_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImages(
        [FromForm] List<IFormFile>? files,
        IFormFile? file,
        CancellationToken ct = default)
        => Ok(await _images.UploadAsync(ToImageFiles(files, file), crabId: null, TryGetUserId(), ct));

    /// <summary>[READ] Get crab by id</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabByIdAsync(id, ct));

    /// <summary>[READ] Hồ sơ vòng đời cua — vị trí, lô, AI, media, timeline, cảnh báo. Không xóa field cũ.</summary>
    [HttpGet("{id:guid}/profile")]
    public async Task<IActionResult> GetProfile(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabProfileAsync(id, ct));

    /// <summary>[READ] Stream 1 ảnh cua (S3 private / local). Desktop dùng kèm Bearer — không phụ thuộc PublicRead.</summary>
    [HttpGet("{id:guid}/photos/{index:int}")]
    public async Task<IActionResult> GetPhoto(Guid id, int index, CancellationToken ct)
    {
        var photo = await _images.GetPhotoAsync(id, index, ct);
        if (photo is null) return NotFound();
        return File(photo.Data, photo.ContentType);
    }

    /// <summary>[CREATE] Place crab — required: crabLotId + boxId (Row/Area auto from box). imageUrls = S3 links from POST /api/crabs/images</summary>
    [HttpPost]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Create([FromBody] CreateCrabRequest req, CancellationToken ct)
        => Ok(await _service.CreateCrabAsync(req, ct));

    /// <summary>[UPDATE] Update crab details. Send imageUrls to replace the full photo list.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCrabRequest req, CancellationToken ct)
        => Ok(await _service.UpdateCrabAsync(id, req, ct));

    /// <summary>[CREATE] Upload more photos to S3 and append URLs on this crab.</summary>
    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    [RequestSizeLimit(30_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImagesForCrab(
        Guid id,
        [FromForm] List<IFormFile>? files,
        IFormFile? file,
        CancellationToken ct = default)
        => Ok(await _images.UploadAsync(ToImageFiles(files, file), id, TryGetUserId(), ct));

    /// <summary>[DELETE] Soft-delete crab (IsAlive=false, free box, keep history)</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => Ok(await _service.DeleteCrabAsync(id, ct));

    /// <summary>[READ] Lịch sử trạng thái cua</summary>
    [HttpGet("{id:guid}/status-history")]
    public async Task<IActionResult> StatusHistory(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabStatusHistoryAsync(id, ct));

    /// <summary>[READ] Lịch sử cân nặng</summary>
    [HttpGet("{id:guid}/weights")]
    public async Task<IActionResult> WeightHistory(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabWeightHistoryAsync(id, ct));

    /// <summary>[READ] Lịch sử AI (không nhập tay)</summary>
    [HttpGet("{id:guid}/ai-analyses")]
    public async Task<IActionResult> AiAnalyses(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabAiAnalysesAsync(id, ct));

    /// <summary>[READ] Lịch sử thu hoạch</summary>
    [HttpGet("{id:guid}/harvests")]
    public async Task<IActionResult> HarvestHistory(Guid id, CancellationToken ct)
        => Ok(await _service.GetCrabHarvestHistoryAsync(id, ct));

    private static IReadOnlyList<CrabSenseBE.Application.Interfaces.CrabImageFile> ToImageFiles(
        List<IFormFile>? files, IFormFile? file)
    {
        var list = new List<IFormFile>();
        if (files is { Count: > 0 })
            list.AddRange(files.Where(f => f is { Length: > 0 }));
        if (file is { Length: > 0 } && list.All(f => f != file))
            list.Add(file);
        return list
            .Select(f => new CrabSenseBE.Application.Interfaces.CrabImageFile(
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
