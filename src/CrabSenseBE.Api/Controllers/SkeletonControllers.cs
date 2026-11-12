using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Api.Controllers;


[ApiController]
[Route("api/price-lists")]
[Authorize]
[Tags("23. CRUD — Price Lists (stub)")]
[Produces("application/json")]
public class PriceListsController : ControllerBase
{
    /// <summary>[READ] Price lists are not implemented by the current sales domain.</summary>
    [HttpGet]
    public IActionResult GetAll() => NotImplemented(
        "Price lists require an approved pricing source and sales-order contract.");

    private ObjectResult NotImplemented(string detail) => Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Price lists are not implemented.",
        detail: detail);
}

[ApiController]
[Route("api/deliveries")]
[Authorize]
[Tags("25. CRUD — Deliveries (stub)")]
[Produces("application/json")]
public class DeliveriesController : ControllerBase
{
    /// <summary>[READ] Deliveries are not implemented by the current order domain.</summary>
    [HttpGet]
    public IActionResult GetAll() => Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Deliveries are not implemented.",
        detail: "A delivery aggregate and its ownership contract have not been approved.");
}

[ApiController]
[Route("api/payments")]
[Authorize]
[Tags("26. CRUD — Payments (stub)")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    /// <summary>[READ] Payments are not implemented by the current order domain.</summary>
    [HttpGet]
    public IActionResult GetAll() => Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Payments are not implemented.",
        detail: "A payment provider and authoritative payment ledger have not been approved.");
}


[ApiController]
[Route("api/settings")]
[Authorize(Roles = AppRoles.FarmManage)]
[Tags("32. CRUD — Farm settings")]
[Produces("application/json")]
public class SettingsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;
    private readonly FarmAreaScope _areas;

    public SettingsController(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    /// <summary>[READ] Cài đặt trại. Không gồm cấu hình hệ thống kỹ thuật.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? category, [FromQuery] Guid? farmingAreaId, CancellationToken ct)
    {
        var allowed = await _areas.SelectionAsync(farmingAreaId, ct) ?? new HashSet<Guid>();
        var items = (await _uow.FarmSettings.GetAllAsync(ct))
            .Where(item => item.FarmingAreaId is Guid id && allowed.Contains(id) && !IsTechnical(item.Category));
        if (!string.IsNullOrWhiteSpace(category))
        {
            var name = category.Trim();
            items = items.Where(item => item.Category.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(ApiResponse<IEnumerable<FarmSettingDto>>.Ok(
            items.OrderBy(item => item.Category).ThenBy(item => item.Key).Select(Map)));
    }

    /// <summary>[READ] Một cài đặt theo key.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key, [FromQuery] Guid? farmingAreaId, CancellationToken ct)
    {
        var areaId = await RequireAreaAsync(farmingAreaId, ct);
        var item = await FindAsync(key, areaId, ct) ?? throw AppException.NotFound("FarmSettings");
        if (IsTechnical(item.Category))
            return Forbid();
        return Ok(ApiResponse<FarmSettingDto>.Ok(Map(item)));
    }

    /// <summary>[UPDATE] Tạo hoặc sửa cài đặt chung của trại.</summary>
    [HttpPut]
    public Task<IActionResult> UpsertRoot([FromBody] UpsertFarmSettingBody req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Key))
            throw AppException.BadRequest("Key is required.");
        return Upsert(req.Key, new UpsertFarmSettingRequest(req.Value, req.Description, req.Category, req.FarmingAreaId), null, ct);
    }

    /// <summary>[UPDATE] Tạo hoặc sửa cài đặt trại.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Upsert(string key, [FromBody] UpsertFarmSettingRequest req, [FromQuery] Guid? farmingAreaId, CancellationToken ct)
    {
        var normalized = NormalizeKey(key);
        var category = string.IsNullOrWhiteSpace(req.Category) ? "general" : req.Category.Trim();
        if (IsTechnical(category))
            throw AppException.Forbidden("Cấu hình hệ thống kỹ thuật thuộc System Administrator.");
        if (string.IsNullOrWhiteSpace(req.Value))
            throw AppException.BadRequest("Value is required.");

        if (req.FarmingAreaId.HasValue && farmingAreaId.HasValue && req.FarmingAreaId != farmingAreaId)
            throw AppException.BadRequest("FarmingAreaId không khớp.");
        var areaId = await RequireAreaAsync(req.FarmingAreaId ?? farmingAreaId, ct);
        var existing = await FindAsync(normalized, areaId, ct);
        if (existing is null)
        {
            existing = new FarmSettings
            {
                Key = normalized,
                FarmingAreaId = areaId,
                Value = req.Value.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                Category = category
            };
            await _uow.FarmSettings.AddAsync(existing, ct);
        }
        else
        {
            if (IsTechnical(existing.Category))
                throw AppException.Forbidden("Cấu hình hệ thống kỹ thuật thuộc System Administrator.");
            existing.Value = req.Value.Trim();
            if (req.Description is not null)
                existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            existing.Category = category;
            _uow.FarmSettings.Update(existing);
        }

        var actorId = _currentUser.IsAuthenticated ? _currentUser.UserId : (Guid?)null;
        await ConfigAudit.WriteAsync(_uow, actorId, "UpsertFarmSetting", "FarmSettings", existing.Id, existing.Key, ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ApiResponse<FarmSettingDto>.Ok(Map(existing), "Saved."));
    }

    /// <summary>[DELETE] Xóa cài đặt trại.</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, [FromQuery] Guid? farmingAreaId, CancellationToken ct)
    {
        var areaId = await RequireAreaAsync(farmingAreaId, ct);
        var existing = await FindAsync(key, areaId, ct) ?? throw AppException.NotFound("FarmSettings");
        if (IsTechnical(existing.Category))
            return Forbid();
        _uow.FarmSettings.Remove(existing);
        var actorId = _currentUser.IsAuthenticated ? _currentUser.UserId : (Guid?)null;
        await ConfigAudit.WriteAsync(_uow, actorId, "DeleteFarmSetting", "FarmSettings", existing.Id, existing.Key, ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("Deleted."));
    }

    private async Task<Guid> RequireAreaAsync(Guid? requestedAreaId, CancellationToken ct)
    {
        if (requestedAreaId is Guid supplied && supplied != Guid.Empty)
        {
            await _areas.EnsureAsync(supplied, manage: true, ct);
            return supplied;
        }
        var assigned = await _areas.AreaIdsAsync(ct) ?? new HashSet<Guid>();
        if (assigned.Count == 0)
            throw AppException.Forbidden("Bạn chưa được phân công khu.");
        if (assigned.Count != 1)
            throw AppException.BadRequest("Chọn FarmingAreaId khi quản lý nhiều khu.");
        return assigned.Single();
    }

    private async Task<FarmSettings?> FindAsync(string key, Guid areaId, CancellationToken ct)
    {
        var normalized = NormalizeKey(key);
        return await _uow.FarmSettings.FirstOrDefaultAsync(
            item => item.FarmingAreaId == areaId && item.Key == normalized, ct);
    }

    private static string NormalizeKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            throw AppException.BadRequest("Key is required.");
        return normalized;
    }

    private static bool IsTechnical(string? category) =>
        category is not null && category.Trim().ToLowerInvariant() is "system" or "technical" or "platform";

    private static FarmSettingDto Map(FarmSettings item) =>
        new(item.Id, item.Key, item.Value, item.Description, item.Category, item.UpdatedAt ?? item.CreatedAt, item.FarmingAreaId);

    private sealed record FarmSettingDto(
        Guid Id, string Key, string Value, string? Description, string Category, DateTime UpdatedAt, Guid? FarmingAreaId);

    public sealed record UpsertFarmSettingRequest(string Value, string? Description = null, string? Category = null, Guid? FarmingAreaId = null);

    public sealed record UpsertFarmSettingBody(string Key, string Value, string? Description = null, string? Category = null, Guid? FarmingAreaId = null);
}

[ApiController]
[Route("api/ai")]
[Authorize]
[Tags("33. AI")]
[Produces("application/json")]
public class AiController : ControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IAiOpsService _ai;
    private readonly ICurrentUserService _currentUser;

    public AiController(
        IDashboardService dashboard,
        IAiOpsService ai,
        ICurrentUserService currentUser)
    {
        _dashboard = dashboard;
        _ai = ai;
        _currentUser = currentUser;
    }

    /// <summary>[READ] List AI detections — optional boxId / mediaId (videoId alias)</summary>
    [HttpGet("detections")]
    public async Task<IActionResult> Detections(
        [FromQuery] Guid? boxId = null,
        [FromQuery] Guid? mediaId = null,
        [FromQuery] Guid? videoId = null,
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] int? take = null,
        CancellationToken ct = default)
        => Ok(await _ai.ListDetectionsAsync(boxId, mediaId ?? videoId, farmingAreaId, take, ct));

    /// <summary>[CREATE] Trigger AI analysis on media/video</summary>
    [HttpPost("analyze")]
    [Authorize(Roles = AppRoles.FarmWrite)]
    public async Task<IActionResult> Analyze(
        [FromBody] AiAnalyzeRequest req, CancellationToken ct = default)
        => Ok(await _ai.AnalyzeAsync(req, ct));

    /// <summary>[CREATE] Submit AI feedback</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(
        [FromBody] AiFeedbackRequest req, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == Guid.Empty)
            throw AppException.Unauthorized();
        return Ok(await _ai.SubmitFeedbackAsync(
            req, _currentUser.UserId, ct));
    }

    /// <summary>[READ] AI recommendations for Home — optional farmingAreaId</summary>
    [HttpGet("recommendations")]
    public async Task<IActionResult> Recommendations(
        [FromQuery] Guid? farmingAreaId = null,
        CancellationToken ct = default)
        => Ok(await _dashboard.GetRecommendationsAsync(farmingAreaId, ct));
}
