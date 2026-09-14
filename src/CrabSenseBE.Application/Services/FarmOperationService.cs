using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class FarmOperationService : IFarmOperationService
{
    private readonly IUnitOfWork _uow;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FarmOperationService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListAllAsync(
        int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(type))
            all = all.Where(o => string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase));
        if (startDate.HasValue)
            all = all.Where(o => o.Timestamp >= startDate.Value);
        if (endDate.HasValue)
            all = all.Where(o => o.Timestamp <= endDate.Value);

        var pageSize = limit <= 0 ? 50 : limit;
        var pageNum = page <= 0 ? 1 : page;
        var items = all.OrderByDescending(o => o.Timestamp)
            .Skip((pageNum - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<FarmOperationDto>>.Ok(items);
    }

    public async Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListByBoxAsync(
        Guid boxId, int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var boxKey = boxId.ToString();
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(o => o.BoxIdsJson.Contains(boxKey, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(type))
            all = all.Where(o => string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase));
        if (startDate.HasValue)
            all = all.Where(o => o.Timestamp >= startDate.Value);
        if (endDate.HasValue)
            all = all.Where(o => o.Timestamp <= endDate.Value);

        var pageSize = limit <= 0 ? 50 : limit;
        var pageNum = page <= 0 ? 1 : page;
        var items = all.OrderByDescending(o => o.Timestamp)
            .Skip((pageNum - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<FarmOperationDto>>.Ok(items);
    }

    public async Task<ApiResponse<FarmOperationDto>> CreateAsync(
        CreateFarmOperationRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Type))
            throw AppException.BadRequest("Type is required.");

        var crabIds = NormalizeIds(req.CrabIds);
        var condition = NormalizeConditionKey(req.Condition);

        var op = new FarmOperation
        {
            Type = req.Type.Trim(),
            BoxIdsJson = JsonSerializer.Serialize(req.BoxIds ?? Array.Empty<string>(), JsonOpts),
            CrabIdsJson = JsonSerializer.Serialize(crabIds, JsonOpts),
            Appetite = NormalizeAppetite(req.Appetite),
            FoodType = string.IsNullOrWhiteSpace(req.FoodType) ? null : req.FoodType.Trim(),
            Condition = condition,
            Quantity = req.Quantity,
            Unit = req.Unit,
            Notes = req.Notes ?? "",
            PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls ?? Array.Empty<string>(), JsonOpts),
            Timestamp = req.Timestamp ?? DateTime.UtcNow,
            OperatorId = req.OperatorId ?? Guid.Empty,
            OperatorName = req.OperatorName ?? "Operator",
            Source = string.IsNullOrWhiteSpace(req.Source) ? "manual" : req.Source.Trim(),
            LocationLabel = req.LocationLabel
        };
        await _uow.FarmOperations.AddAsync(op, ct);

        // Đánh dấu tình trạng khi cho ăn → cập nhật luôn con cua, để hộp/hồ sơ/
        // cảnh báo thấy ngay. Ghi chung 1 SaveChanges với phiếu: hoặc cả hai, hoặc không.
        await ApplyConditionToCrabsAsync(crabIds, condition, op.Timestamp, ct);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmOperationDto>.Ok(Map(op), "Created.");
    }

    public async Task<ApiResponse<FarmOperationDto>> UpdateAsync(
        Guid id, UpdateFarmOperationRequest req, CancellationToken ct = default)
    {
        var op = await _uow.FarmOperations.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Farm operation");
        if (op.Timestamp < DateTime.UtcNow.AddHours(-24))
            throw AppException.BadRequest("Operation can only be edited within 24 hours.");

        if (!string.IsNullOrWhiteSpace(req.Type)) op.Type = req.Type.Trim();
        if (req.BoxIds is not null) op.BoxIdsJson = JsonSerializer.Serialize(req.BoxIds, JsonOpts);
        if (req.CrabIds is not null) op.CrabIdsJson = JsonSerializer.Serialize(NormalizeIds(req.CrabIds), JsonOpts);
        if (req.Appetite is not null) op.Appetite = NormalizeAppetite(req.Appetite);
        if (req.FoodType is not null) op.FoodType = string.IsNullOrWhiteSpace(req.FoodType) ? null : req.FoodType.Trim();
        if (req.Condition is not null) op.Condition = NormalizeConditionKey(req.Condition);
        if (req.Notes is not null) op.Notes = req.Notes;
        if (req.Quantity.HasValue) op.Quantity = req.Quantity;
        if (req.Unit is not null) op.Unit = req.Unit;
        if (req.PhotoUrls is not null)
            op.PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls, JsonOpts);
        op.UpdatedAt = DateTime.UtcNow;

        _uow.FarmOperations.Update(op);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmOperationDto>.Ok(Map(op), "Updated.");
    }

    public async Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListByCrabAsync(
        Guid crabId, int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var crabKey = crabId.ToString();
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(o => o.CrabIdsJson.Contains(crabKey, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(type))
            all = all.Where(o => string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase));
        if (startDate.HasValue)
            all = all.Where(o => o.Timestamp >= startDate.Value);
        if (endDate.HasValue)
            all = all.Where(o => o.Timestamp <= endDate.Value);

        var pageSize = limit <= 0 ? 50 : limit;
        var pageNum = page <= 0 ? 1 : page;
        var items = all.OrderByDescending(o => o.Timestamp)
            .Skip((pageNum - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<FarmOperationDto>>.Ok(items);
    }

    private static FarmOperationDto Map(FarmOperation o)
    {
        var boxIds = ParseStringList(o.BoxIdsJson);
        var crabIds = ParseStringList(o.CrabIdsJson);
        var photos = ParseStringList(o.PhotoUrlsJson);
        return new FarmOperationDto(
            o.Id, o.Type, boxIds, o.Quantity, o.Unit, o.Notes, photos,
            o.Timestamp, o.OperatorId, o.OperatorName, o.Source, o.LocationLabel,
            crabIds, o.Appetite, o.FoodType, o.Condition);
    }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0) return Array.Empty<string>();
        var outIds = new List<string>(raw.Count);
        foreach (var id in raw)
        {
            if (Guid.TryParse(id, out var g) && !outIds.Contains(g.ToString()))
                outIds.Add(g.ToString());
        }
        return outIds;
    }

    private static string? NormalizeAppetite(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant();
        return key switch
        {
            "many" or "nhieu" or "annhieu" => "many",
            "little" or "it" or "anit" => "little",
            "none" or "khong" or "khongan" or "khong an" => "none",
            _ => null
        };
    }

    /// <summary>Chuẩn hoá nhãn phiếu thành key: normal | premolt | attention | weak.</summary>
    private static string? NormalizeConditionKey(string? raw)
    {
        var key = (raw ?? "").Trim().ToLowerInvariant()
            .Replace("_", "").Replace("-", "").Replace(" ", "");
        return key switch
        {
            "normal" or "binhthuong" => "normal",
            "premolt" or "saplot" => "premolt",
            "attention" or "canchuy" or "problem" or "covande" => "attention",
            "weak" or "yeu" or "cuayeu" => "weak",
            _ => null
        };
    }

    private static CrabCondition? ConditionFromKey(string? key) => key switch
    {
        "normal" => CrabCondition.Normal,
        "premolt" => CrabCondition.Premolt,
        "attention" => CrabCondition.Problem,
        "weak" => CrabCondition.Weak,
        _ => null
    };

    /// <summary>
    /// Đồng bộ tình trạng của các cua trong phiếu + ghi CrabStatusHistory để
    /// hồ sơ cua / cảnh báo thấy được. Không đổi nếu tình trạng y hệt (tránh rác lịch sử).
    /// </summary>
    private async Task ApplyConditionToCrabsAsync(
        IReadOnlyList<string> crabIds, string? conditionKey, DateTime at, CancellationToken ct)
    {
        var next = ConditionFromKey(conditionKey);
        if (next is null || crabIds.Count == 0) return;

        foreach (var raw in crabIds)
        {
            if (!Guid.TryParse(raw, out var crabId)) continue;
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
            if (crab is null) continue;

            var oldCondition = crab.Condition;
            var oldStatus = crab.Status;
            if (oldCondition == next.Value) continue;

            crab.Condition = next.Value;
            crab.Status = CrabConditions.ToLifecycle(next.Value);
            crab.UpdatedAt = at;
            _uow.Crabs.Update(crab);

            await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
            {
                CrabId = crab.Id,
                OldCondition = oldCondition,
                NewCondition = crab.Condition,
                OldStatus = oldStatus,
                NewStatus = crab.Status,
                ChangedAt = at,
                Source = "manual",
                Reason = "Ghi nhận khi cho ăn"
            }, ct);
        }
    }

    private static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
