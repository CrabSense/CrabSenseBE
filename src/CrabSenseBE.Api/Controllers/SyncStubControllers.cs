using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Enums;
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

    public SyncQueueStubController(AppDbContext db) => _db = db;

    /// <summary>
    /// Accepts idempotent mobile mutations. Domain-specific handlers can
    /// consume each item while clients already have a stable sync contract.
    /// </summary>
    [HttpPost("/api/sync/batch")]
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

            if (string.Equals(item.EntityType, "crab", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.OperationType, "update_crab", StringComparison.OrdinalIgnoreCase))
            {
                var payload = item.Payload ?? new Dictionary<string, object?>();
                var statusValue = payload.TryGetValue("status", out var rawStatus)
                    ? rawStatus
                    : null;
                if (statusValue == null ||
                    !Enum.TryParse<CrabStatus>(
                        statusValue.ToString(),
                        ignoreCase: true,
                        out var status) ||
                    !Guid.TryParse(item.EntityId, out var crabId))
                {
                    inboxItem.Status = "failed";
                    inboxItem.ErrorMessage =
                        "update_crab chỉ nhận payload.status là trạng thái CrabStatus hợp lệ.";
                    failed.Add(item.IdempotencyKey);
                    continue;
                }

                var crab = await _db.Crabs.FindAsync(crabId);
                if (crab == null)
                {
                    inboxItem.Status = "failed";
                    inboxItem.ErrorMessage = "Không tìm thấy cua theo entityId.";
                    failed.Add(item.IdempotencyKey);
                    continue;
                }

                var clientTime = SyncBatchItem.ResolveClientUpdatedAt(item);
                var serverTime = crab.UpdatedAt ?? crab.CreatedAt;
                if (clientTime == null || serverTime.ToUniversalTime() > clientTime.Value.UtcDateTime)
                {
                    inboxItem.Status = "processed";
                    inboxItem.ProcessedAt = DateTimeOffset.UtcNow;
                    inboxItem.ErrorMessage =
                        "Giữ bản server vì updatedAt mới hơn clientUpdatedAt.";
                    processed.Add(item.IdempotencyKey);
                    continue;
                }

                crab.Status = status;
                crab.UpdatedAt = clientTime.Value.UtcDateTime;
                inboxItem.Status = "processed";
                inboxItem.ProcessedAt = DateTimeOffset.UtcNow;
                processed.Add(item.IdempotencyKey);
            }
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> PullEntities(
        [FromQuery] DateTimeOffset? since = null,
        [FromQuery] Guid? farmingAreaId = null,
        [FromQuery] int limit = 200)
    {
        limit = Math.Clamp(limit, 1, 500);
        var sinceUtc = since?.UtcDateTime;

        var boxQuery = _db.Boxes.AsNoTracking().AsQueryable();
        if (farmingAreaId.HasValue)
        {
            boxQuery = boxQuery.Where(
                box => box.FarmingRow != null &&
                       box.FarmingRow.FarmingAreaId == farmingAreaId.Value);
        }
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

        var crabQuery = _db.Crabs.AsNoTracking().AsQueryable();
        if (farmingAreaId.HasValue)
        {
            crabQuery = crabQuery.Where(
                crab => crab.Box != null &&
                        crab.Box.FarmingRow != null &&
                        crab.Box.FarmingRow.FarmingAreaId == farmingAreaId.Value);
        }
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
    /// <summary>[READ] Hàng đợi sync (stub)</summary>
    [HttpGet("/api/sync/queue")]
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

[ApiController]
[Authorize]
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
[Authorize]
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
