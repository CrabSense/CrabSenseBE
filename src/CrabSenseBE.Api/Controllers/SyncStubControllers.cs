using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CrabSenseBE.Api.Controllers;

/// <summary>
/// Stub sync endpoints cho FE / Kiosk — trả skeleton, bổ sung logic sau.
/// Giữ tách khỏi <see cref="SyncController"/> (HDF5).
/// </summary>
[ApiController]
[Authorize]
[Tags("09b. Sync stubs (FE)")]
[Produces("application/json")]
public class SyncQueueStubController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUnitOfWork _uow;
    private readonly IFarmHistoryService _farmHistory;
    private readonly FarmAreaScope _areas;

    public SyncQueueStubController(
        AppDbContext db,
        IUnitOfWork uow,
        IFarmHistoryService farmHistory,
        ICurrentUserService currentUser)
    {
        _db = db;
        _uow = uow;
        _farmHistory = farmHistory;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    /// <summary>
    /// Accepts idempotent mobile mutations. Domain-specific handlers can
    /// consume each item while clients already have a stable sync contract.
    /// </summary>
    [HttpPost("/api/sync/batch")]
    [Authorize(Roles = AppRoles.FarmStaff)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Batch([FromBody] SyncBatchRequest request)
    {
        var items = request.Items ?? Array.Empty<SyncBatchItem>();
        var accepted = new List<string>();
        var processed = new List<string>();
        var failed = new List<string>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.IdempotencyKey))
                continue;

            var existing = await _db.SyncInboxItems.FirstOrDefaultAsync(
                stored => stored.IdempotencyKey == item.IdempotencyKey);
            if (existing != null)
            {
                accepted.Add(item.IdempotencyKey);
                if (existing.Status == "processed")
                    processed.Add(item.IdempotencyKey);
                continue;
            }

            var inboxItem = new CrabSenseBE.Domain.Entities.SyncInboxItem
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = item.IdempotencyKey,
                EntityType = item.EntityType ?? "unknown",
                EntityId = item.EntityId ?? string.Empty,
                OperationType = item.OperationType ?? "unknown",
                BaseVersion = item.BaseVersion,
                ClientUpdatedAt = item.ClientUpdatedAt,
                PayloadJson = JsonSerializer.Serialize(item.Payload ?? new Dictionary<string, object?>()),
                ReceivedAt = DateTimeOffset.UtcNow
            };
            _db.SyncInboxItems.Add(inboxItem);
            accepted.Add(item.IdempotencyKey);

            await ApplyMobileOp(item, inboxItem, processed, failed);
        }

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            success = true,
            processedCount = processed.Count,
            acceptedItemIds = accepted,
            processedItemIds = processed,
            failedItemIds = failed,
            conflictItemIds = Array.Empty<string>(),
            conflicts = Array.Empty<object>()
        }, "Batch accepted"));
    }

    /// <summary>
    /// Incremental pull: only boxes/crabs changed after [since], optionally
    /// scoped to one farming area so other farms are not downloaded.
    /// </summary>
    [HttpGet("/api/sync/pull")]
    [Authorize(Roles = AppRoles.FarmPortal)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PullEntities(
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var sinceUtc = since?.UtcDateTime;
        var allowedAreas = await _areas.SelectionAsync(farmingAreaId, ct)
            ?? new HashSet<Guid>();

        var boxQuery = _db.Boxes.AsNoTracking().Where(
            box => box.FarmingRow != null
                && allowedAreas.Contains(box.FarmingRow.FarmingAreaId));
        if (sinceUtc.HasValue)
        {
            boxQuery = boxQuery.Where(
                box => (box.UpdatedAt ?? box.CreatedAt) >= sinceUtc.Value);
        }

        var boxes = await boxQuery
            .OrderBy(box => box.UpdatedAt ?? box.CreatedAt)
            .Take(limit)
            .Select(box => new
            {
                id = box.Id,
                code = box.Code,
                status = box.Status,
                isOccupied = box.IsOccupied,
                farmingRowId = box.FarmingRowId,
                farmingAreaId = box.FarmingRow != null
                    ? box.FarmingRow.FarmingAreaId
                    : Guid.Empty,
                updatedAt = box.UpdatedAt ?? box.CreatedAt,
                createdAt = box.CreatedAt
            })
            .ToListAsync();

        var crabQuery = _db.Crabs.AsNoTracking().Where(
            crab => crab.Box != null
                && crab.Box.FarmingRow != null
                && allowedAreas.Contains(crab.Box.FarmingRow.FarmingAreaId));
        if (sinceUtc.HasValue)
        {
            crabQuery = crabQuery.Where(
                crab => (crab.UpdatedAt ?? crab.CreatedAt) >= sinceUtc.Value);
        }

        var crabs = await crabQuery
            .OrderBy(crab => crab.UpdatedAt ?? crab.CreatedAt)
            .Take(limit)
            .Select(crab => new
            {
                id = crab.Id,
                boxId = crab.BoxId,
                code = crab.Code,
                tag = crab.Tag,
                status = crab.Status.ToString(),
                condition = crab.Condition.ToString(),
                weightGram = crab.WeightGram,
                updatedAt = crab.UpdatedAt ?? crab.CreatedAt,
                stockedAt = crab.StockedAt,
                createdAt = crab.CreatedAt
            })
            .ToListAsync();

        var cursor = DateTimeOffset.UtcNow;
        return Ok(ApiResponse<object>.Ok(new
        {
            since,
            farmingAreaId,
            cursor = cursor.ToString("O"),
            changes = new { box = boxes, crab = crabs }
        }, "Sync pull"));
    }

    async Task ApplyMobileOp(
        SyncBatchItem item,
        SyncInboxItem inboxItem,
        List<string> processed,
        List<string> failed)
    {
        var op = item.OperationType ?? "";
        try
        {
            if (op.Equals("update_crab", StringComparison.OrdinalIgnoreCase))
                await ApplyUpdateCrab(item, inboxItem, processed, failed);
            else if (op.Equals("create_crab", StringComparison.OrdinalIgnoreCase))
                await ApplyCreateCrab(item, inboxItem, processed, failed);
            else if (op.Equals("transfer_crab", StringComparison.OrdinalIgnoreCase))
                await ApplyTransfer(item, inboxItem, processed, failed);
            else if (op.Equals("update_box", StringComparison.OrdinalIgnoreCase))
                await ApplyUpdateBox(item, inboxItem, processed, failed);
        }
        catch (AppException ex)
        {
            inboxItem.Status = "failed";
            inboxItem.ErrorMessage = ex.Message;
            failed.Add(item.IdempotencyKey!);
        }
    }

    async Task ApplyUpdateCrab(
        SyncBatchItem item, SyncInboxItem inbox, List<string> processed, List<string> failed)
    {
        var payload = SyncPayload.MergeNested(item.Payload, "crab");
        var statusRaw = SyncPayload.Str(payload, "status");
        if (statusRaw == null ||
            !Enum.TryParse<CrabStatus>(statusRaw, ignoreCase: true, out var status) ||
            !Enum.IsDefined(status) ||
            !Guid.TryParse(item.EntityId, out var crabId))
        {
            Fail(inbox, failed, item, "update_crab cần entityId + payload.status (CrabStatus).");
            return;
        }

        var crab = await _db.Crabs.FindAsync(crabId);
        if (crab == null)
        {
            Fail(inbox, failed, item, "Không tìm thấy cua theo entityId.");
            return;
        }
        var currentBoxId = crab.BoxId
            ?? (await _uow.CrabBoxAllocations.FindAsync(
                    allocation => allocation.CrabId == crab.Id
                        && allocation.EndTime == null,
                    HttpContext.RequestAborted))
                .OrderByDescending(allocation => allocation.StartTime)
                .FirstOrDefault()?.BoxId;
        if (currentBoxId is not Guid scopedBoxId)
        {
            Fail(inbox, failed, item, "Không xác định được khu hiện tại của cua.");
            return;
        }
        await _areas.EnsureBoxAsync(
            scopedBoxId, manage: false, HttpContext.RequestAborted);
        if (status is CrabStatus.Dead or CrabStatus.Harvested or CrabStatus.Sold)
        {
            Fail(inbox, failed, item,
                "Trạng thái chết hoặc thu hoạch phải dùng quy trình nghiệp vụ có audit.");
            return;
        }

        if (ServerWins(item, crab.UpdatedAt ?? crab.CreatedAt))
        {
            KeepServer(inbox, processed, item);
            return;
        }

        var oldStatus = crab.Status;
        var oldCondition = crab.Condition;
        crab.Status = status;
        crab.Condition = CrabConditions.FromMoltingAndStatus(
            crab.MoltingStage, status);
        crab.UpdatedAt = SyncBatchItem.ResolveClientUpdatedAt(item)!.Value.UtcDateTime;
        _db.CrabStatusHistories.Add(new CrabStatusHistory
        {
            CrabId = crab.Id,
            OldCondition = oldCondition,
            NewCondition = crab.Condition,
            OldStatus = oldStatus,
            NewStatus = status,
            ChangedAt = DateTime.UtcNow,
            Source = "offline-sync",
            ChangedByUserId = (await _areas.CurrentUserAsync(
                HttpContext.RequestAborted))?.Id
        });
        Done(inbox, processed, item);
    }

    async Task ApplyCreateCrab(
        SyncBatchItem item, SyncInboxItem inbox, List<string> processed, List<string> failed)
    {
        var payload = SyncPayload.MergeNested(item.Payload, "crab");
        if (!Guid.TryParse(item.EntityId, out var crabId))
        {
            Fail(inbox, failed, item, "create_crab cần entityId Guid.");
            return;
        }

        var crab = await _db.Crabs.FindAsync(crabId);
        if (crab == null)
        {
            var targetBoxRaw = SyncPayload.Str(payload, "boxId")
                ?? SyncPayload.Str(item.Payload ?? new(), "boxId");
            if (!Guid.TryParse(targetBoxRaw, out var targetBoxId) || targetBoxId == Guid.Empty)
            {
                Fail(inbox, failed, item, "create_crab cần boxId hợp lệ.");
                return;
            }
            try
            {
                await _areas.EnsureBoxAsync(targetBoxId, manage: false, HttpContext.RequestAborted);
            }
            catch (AppException ex)
            {
                Fail(inbox, failed, item, ex.Message);
                return;
            }

            var targetBox = await _uow.Boxes.GetByIdAsync(targetBoxId, HttpContext.RequestAborted);
            var targetRow = targetBox is null
                ? null
                : await _uow.FarmingRows.GetByIdAsync(
                    targetBox.FarmingRowId, HttpContext.RequestAborted);
            if (targetRow is null)
            {
                Fail(inbox, failed, item, "Không xác định được khu của hộp đích.");
                return;
            }

            var linkedLotIds = (await _uow.CrabLotAreas.FindAsync(
                    link => link.FarmingAreaId == targetRow.FarmingAreaId,
                    HttpContext.RequestAborted))
                .Select(link => link.CrabLotId)
                .ToHashSet();
            var lots = (await _uow.CrabLots.GetAllAsync(HttpContext.RequestAborted))
                .Where(lot => linkedLotIds.Contains(lot.Id)
                    && lot.ClosedAt is null
                    && !string.Equals(lot.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .OrderBy(lot => lot.CreatedAt)
                .ToList();
            CrabLot? selectedLot = null;
            foreach (var candidate in lots)
            {
                var placed = await _uow.Crabs.CountAsync(
                    row => row.CrabLotId == candidate.Id,
                    HttpContext.RequestAborted);
                if (placed < candidate.Quantity - candidate.DeadOnArrival)
                {
                    selectedLot = candidate;
                    break;
                }
            }
            if (selectedLot is null)
            {
                Fail(inbox, failed, item,
                    "Không có Crab Lot đang mở, thuộc khu hộp đích và còn sức chứa.");
                return;
            }
            var lotId = selectedLot.Id;
            try
            {
                _ = await CrabLotRegistrationGuard.EnsureCanRegisterAsync(
                    _uow, lotId, adding: 1, HttpContext.RequestAborted);
                _ = await CrabLotRegistrationGuard.EnsureLotAllowsBoxAsync(
                    _uow, lotId, targetBoxId, HttpContext.RequestAborted);
            }
            catch (AppException ex)
            {
                Fail(inbox, failed, item, ex.Message);
                return;
            }

            var code = SyncPayload.Str(payload, "code", "tag") ?? $"SYNC-{crabId.ToString("N")[..8]}";
            crab = new Crab
            {
                Id = crabId,
                CrabLotId = lotId,
                Code = code,
                QrCode = $"QR-{code}",
                Tag = SyncPayload.Str(payload, "tag") ?? code,
                WeightGram = SyncPayload.Dec(payload, "weight", "weightGram"),
                StockedAt = DateTime.UtcNow,
                MoltingStage = SyncPayload.Str(payload, "moltingStatus", "moltingStage") ?? "",
            };
            if (Enum.TryParse<CrabStatus>(
                    SyncPayload.Str(payload, "status") ?? "", true, out var st)
                && Enum.IsDefined(st))
            {
                if (st is CrabStatus.Dead or CrabStatus.Harvested or CrabStatus.Sold)
                {
                    Fail(inbox, failed, item,
                        "Không thể đăng ký cua mới với trạng thái chết hoặc thu hoạch.");
                    return;
                }
                crab.Status = st;
            }
            _db.Crabs.Add(crab);
        }

        var boxRaw = SyncPayload.Str(payload, "boxId") ?? SyncPayload.Str(item.Payload ?? new(), "boxId");
        if (Guid.TryParse(boxRaw, out var boxId) && boxId != Guid.Empty)
        {
            await _farmHistory.TransferCrabAsync(new MobileTransferCrabRequest(crabId, boxId));
        }

        Done(inbox, processed, item);
    }

    async Task ApplyTransfer(
        SyncBatchItem item, SyncInboxItem inbox, List<string> processed, List<string> failed)
    {
        if (!Guid.TryParse(item.EntityId, out var crabId))
        {
            Fail(inbox, failed, item, "transfer_crab cần entityId Guid.");
            return;
        }

        var payload = item.Payload ?? new Dictionary<string, object?>();
        if (!Guid.TryParse(SyncPayload.Str(payload, "destinationBoxId"), out var dest) || dest == Guid.Empty)
        {
            Fail(inbox, failed, item, "transfer_crab cần destinationBoxId.");
            return;
        }

        Guid? source = Guid.TryParse(SyncPayload.Str(payload, "sourceBoxId"), out var src) ? src : null;
        await _farmHistory.TransferCrabAsync(new MobileTransferCrabRequest(crabId, dest, source));
        Done(inbox, processed, item);
    }

    async Task ApplyUpdateBox(
        SyncBatchItem item, SyncInboxItem inbox, List<string> processed, List<string> failed)
    {
        var payload = SyncPayload.MergeNested(item.Payload, "box");
        if (!Guid.TryParse(item.EntityId, out var boxId))
        {
            Fail(inbox, failed, item, "update_box cần entityId Guid.");
            return;
        }

        var box = await _db.Boxes.FindAsync(boxId);
        if (box == null)
        {
            Fail(inbox, failed, item, "Không tìm thấy hộp.");
            return;
        }
        await _areas.EnsureBoxAsync(
            box.Id, manage: false, HttpContext.RequestAborted);

        if (ServerWins(item, box.UpdatedAt ?? box.CreatedAt))
        {
            KeepServer(inbox, processed, item);
            return;
        }

        var code = SyncPayload.Str(payload, "code", "qrCode");
        if (!string.IsNullOrWhiteSpace(code))
        {
            Fail(inbox, failed, item,
                "Farm Staff không được sửa mã hộp qua đồng bộ vận hành.");
            return;
        }
        var status = SyncPayload.Str(payload, "status");
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!BoxStatuses.IsValid(status))
            {
                Fail(inbox, failed, item,
                    $"Trạng thái hộp không hợp lệ: {status}.");
                return;
            }
            var oldStatus = box.Status;
            var nextStatus = BoxStatuses.Normalize(status);
            box.Status = nextStatus;
            if (!string.Equals(oldStatus, nextStatus, StringComparison.OrdinalIgnoreCase))
            {
                _db.BoxStatusHistories.Add(new BoxStatusHistory
                {
                    BoxId = box.Id,
                    OldStatus = oldStatus,
                    NewStatus = nextStatus,
                    OldIsOccupied = box.IsOccupied,
                    NewIsOccupied = box.IsOccupied,
                    ChangedAt = DateTime.UtcNow,
                    Reason = "Offline sync",
                    ChangedByUserId = (await _areas.CurrentUserAsync(
                        HttpContext.RequestAborted))?.Id
                });
            }
        }
        box.UpdatedAt = SyncBatchItem.ResolveClientUpdatedAt(item)!.Value.UtcDateTime;
        Done(inbox, processed, item);
    }

    static bool ServerWins(SyncBatchItem item, DateTime serverTime)
    {
        var clientTime = SyncBatchItem.ResolveClientUpdatedAt(item);
        return clientTime == null || serverTime.ToUniversalTime() > clientTime.Value.UtcDateTime;
    }

    static void KeepServer(SyncInboxItem inbox, List<string> processed, SyncBatchItem item)
    {
        inbox.Status = "processed";
        inbox.ProcessedAt = DateTimeOffset.UtcNow;
        inbox.ErrorMessage = "Giữ bản server vì updatedAt mới hơn clientUpdatedAt.";
        processed.Add(item.IdempotencyKey!);
    }

    static void Done(SyncInboxItem inbox, List<string> processed, SyncBatchItem item)
    {
        inbox.Status = "processed";
        inbox.ProcessedAt = DateTimeOffset.UtcNow;
        processed.Add(item.IdempotencyKey!);
    }

    static void Fail(SyncInboxItem inbox, List<string> failed, SyncBatchItem item, string message)
    {
        inbox.Status = "failed";
        inbox.ErrorMessage = message;
        failed.Add(item.IdempotencyKey!);
    }

    /// <summary>[READ] Hàng đợi sync (stub)</summary>
    [HttpGet("/api/sync/queue")]
    [Authorize(Roles = AppRoles.Platform)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSyncQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.SyncInboxItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(item => item.Status == status);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(item => item.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.Id,
                item.IdempotencyKey,
                item.EntityType,
                item.EntityId,
                item.OperationType,
                item.BaseVersion,
                item.ClientUpdatedAt,
                item.Status,
                item.ErrorMessage,
                item.ReceivedAt,
                item.ProcessedAt
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            status
        }, "Sync queue"));
    }

    /// <summary>[CREATE] Enqueue sync job (stub)</summary>
    [HttpPost("/api/sync/queue")]
    [Authorize(Roles = AppRoles.Platform)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult EnqueueSync([FromBody] object? body)
        => Ok(ApiResponse<object>.Ok(new
        {
            id = Guid.Empty,
            status = "queued",
            received = body,
            stub = true
        }, "TODO: implement POST /api/sync/queue"));
}

public sealed class SyncBatchRequest
{
    public IReadOnlyList<SyncBatchItem>? Items { get; init; }
}

public sealed class SyncBatchItem
{
    public string? IdempotencyKey { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? OperationType { get; init; }
    public int? BaseVersion { get; init; }
    public DateTimeOffset? ClientUpdatedAt { get; init; }
    public Dictionary<string, object?>? Payload { get; init; }

    internal static DateTimeOffset? ResolveClientUpdatedAt(SyncBatchItem item)
    {
        if (item.ClientUpdatedAt.HasValue)
            return item.ClientUpdatedAt.Value.ToUniversalTime();
        if (item.Payload != null &&
            item.Payload.TryGetValue("clientUpdatedAt", out var raw) &&
            raw != null &&
            DateTimeOffset.TryParse(raw.ToString(), out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }
}

internal static class SyncPayload
{
    public static Dictionary<string, object?> MergeNested(
        Dictionary<string, object?>? payload, string nestedKey)
    {
        var root = payload ?? new Dictionary<string, object?>();
        if (!root.TryGetValue(nestedKey, out var raw) || raw is null)
            return root;
        if (raw is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            var nested = JsonSerializer.Deserialize<Dictionary<string, object?>>(el.GetRawText());
            if (nested == null) return root;
            foreach (var pair in root)
            {
                if (!nested.ContainsKey(pair.Key))
                    nested[pair.Key] = pair.Value;
            }
            return nested;
        }

        return root;
    }

    public static string? Str(IDictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!payload.TryGetValue(key, out var raw) || raw is null) continue;
            if (raw is JsonElement el)
            {
                if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            }

            var text = raw.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }

    public static decimal? Dec(IDictionary<string, object?> payload, params string[] keys)
    {
        var text = Str(payload, keys);
        return decimal.TryParse(text, out var value) ? value : null;
    }
}

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/v1/sync")]
[Tags("09b. Sync stubs (FE)")]
[Produces("application/json")]
public class SyncV1StubController : ControllerBase
{
    private readonly AppDbContext _db;

    public SyncV1StubController(AppDbContext db) => _db = db;

    /// <summary>[READ/ACTION] Pull sync payload (stub) — FE có thể GET hoặc POST</summary>
    [HttpGet("pull")]
    [HttpPost("pull")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pull(
        [FromQuery] DateTime? since = null,
        [FromQuery] string? deviceCode = null,
        [FromBody] object? body = null)
    {
        var changes = await GetChangesQuery(since, null, null, 1, 100).ToListAsync();
        return Ok(ApiResponse<object>.Ok(new
        {
            since,
            deviceCode,
            cursor = DateTimeOffset.UtcNow.ToString("O"),
            changes,
            request = body
        }, "Sync pull"));
    }

    /// <summary>[READ] Danh sách thay đổi từ mốc thời gian (stub)</summary>
    [HttpGet("changes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Changes(
        [FromQuery] DateTime? since = null,
        [FromQuery] DateTime? until = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entity = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = GetChangesQuery(since, until, entity, page, pageSize);
        var items = await query.ToListAsync();
        var countQuery = GetChangesQuery(since, until, entity, 1, null);
        var totalCount = await countQuery.CountAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            since,
            until,
            entity,
            page,
            pageSize,
            items,
            totalCount
        }, "Sync changes"));
    }

    private IQueryable<object> GetChangesQuery(
        DateTime? since,
        DateTime? until,
        string? entity,
        int page,
        int? pageSize)
    {
        var query = _db.SyncInboxItems.AsNoTracking().AsQueryable();
        if (since.HasValue)
            query = query.Where(item => item.ReceivedAt >= since.Value);
        if (until.HasValue)
            query = query.Where(item => item.ReceivedAt <= until.Value);
        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(item => item.EntityType == entity);

        var projected = query
            .OrderBy(item => item.ReceivedAt)
            .Select(item => (object)new
            {
                item.Id,
                item.IdempotencyKey,
                item.EntityType,
                item.EntityId,
                item.OperationType,
                item.BaseVersion,
                item.ClientUpdatedAt,
                item.Status,
                item.ReceivedAt,
                item.ProcessedAt
            });
        if (pageSize.HasValue)
            projected = projected.Skip((page - 1) * pageSize.Value).Take(pageSize.Value);
        return projected;
    }

    /// <summary>[READ] Download artifact / file sync (stub)</summary>
    [HttpGet("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Download(
        [FromQuery] Guid? id = null,
        [FromQuery] string? code = null,
        [FromQuery] string? type = null)
        => Ok(ApiResponse<object>.Ok(new
        {
            id,
            code,
            type,
            url = (string?)null,
            contentType = (string?)null,
            sizeBytes = 0,
            stub = true
        }, "TODO: implement /api/v1/sync/download — có thể đổi sang FileResult sau"));
}

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/v1/synchronization")]
[Tags("09b. Sync stubs (FE)")]
[Produces("application/json")]
public class SynchronizationV1StubController : ControllerBase
{
    private readonly AppDbContext _db;

    public SynchronizationV1StubController(AppDbContext db) => _db = db;

    /// <summary>[READ] Hàng đợi synchronization v1 (stub) — alias FE</summary>
    [HttpGet("queue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _db.SyncInboxItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(item => item.Status == status);
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(item => item.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new
            {
                item.Id,
                item.IdempotencyKey,
                item.EntityType,
                item.EntityId,
                item.OperationType,
                item.Status,
                item.ReceivedAt,
                item.ProcessedAt
            })
            .ToListAsync();
        return Ok(ApiResponse<object>.Ok(
            new { items, totalCount, page, pageSize, status },
            "Synchronization queue"));
    }

    /// <summary>[CREATE] Enqueue synchronization job (stub)</summary>
    [HttpPost("queue")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Enqueue([FromBody] object? body)
        => Ok(ApiResponse<object>.Ok(new
        {
            id = Guid.Empty,
            status = "queued",
            received = body,
            stub = true
        }, "TODO: implement POST /api/v1/synchronization/queue"));
}
