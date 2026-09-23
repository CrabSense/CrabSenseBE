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

    public async Task<ApiResponse<IEnumerable<FeedingHistoryDayDto>>> GetFeedingHistoryAsync(
        int days = 7, CancellationToken ct = default)
    {
        var end = DateTime.UtcNow.Date.AddDays(1);
        var start = end.AddDays(-(Math.Clamp(days, 1, 31)));
        var operations = await _uow.FarmOperations.GetAllAsync(ct);
        var totals = Enumerable.Range(0, (end.Date - start.Date).Days)
            .Select(offset => start.Date.AddDays(offset))
            .ToDictionary(date => date, _ => new int[3]);

        foreach (var operation in operations)
        {
            var isFeedingRecord =
                string.Equals(operation.Type, "feeding", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(operation.Type, "inspection", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(operation.Appetite));
            if (!isFeedingRecord ||
                operation.Timestamp < start ||
                operation.Timestamp >= end)
                continue;

            var appetite = operation.Appetite?.Trim().ToLowerInvariant();
            var bucket = appetite switch
            {
                "many" => 0,
                "little" => 1,
                "none" => 2,
                _ => -1
            };
            if (bucket < 0) continue;

            var crabIds = ParseStringList(operation.CrabIdsJson);
            if (crabIds.Count == 0) continue;
            totals[operation.Timestamp.Date][bucket] += crabIds.Count;
        }

        var result = totals
            .OrderBy(item => item.Key)
            .Select(item => new FeedingHistoryDayDto(
                DateOnly.FromDateTime(item.Key),
                item.Value[0],
                item.Value[1],
                item.Value[2]));
        return ApiResponse<IEnumerable<FeedingHistoryDayDto>>.Ok(result);
    }

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
        ValidateFeedingAmounts(req.Quantity, req.EatenQuantity);

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
            EatenQuantity = req.EatenQuantity,
            ActivityBefore = ClampScore(req.ActivityBefore),
            ActivityAfter = ClampScore(req.ActivityAfter),
            FeedingDurationMinutes = req.FeedingDurationMinutes,
            CameraId = string.IsNullOrWhiteSpace(req.CameraId) ? null : req.CameraId.Trim(),
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
        if (req.EatenQuantity.HasValue) op.EatenQuantity = req.EatenQuantity;
        if (req.ActivityBefore.HasValue) op.ActivityBefore = ClampScore(req.ActivityBefore);
        if (req.ActivityAfter.HasValue) op.ActivityAfter = ClampScore(req.ActivityAfter);
        if (req.FeedingDurationMinutes.HasValue) op.FeedingDurationMinutes = req.FeedingDurationMinutes;
        if (req.CameraId is not null) op.CameraId = string.IsNullOrWhiteSpace(req.CameraId) ? null : req.CameraId.Trim();
        ValidateFeedingAmounts(op.Quantity, op.EatenQuantity);
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
            crabIds, o.Appetite, o.FoodType, o.Condition,
            o.EatenQuantity, FeedingPercentOf(o),
            o.ActivityBefore, o.ActivityAfter, o.FeedingDurationMinutes, o.CameraId);
    }

    // ------------------------------------------------------------------
    // Tab "Ăn & Vận động" — per-crab feeding + activity analytics
    // ------------------------------------------------------------------

    public async Task<ApiResponse<CrabFeedingActivityDto>> GetCrabFeedingActivityAsync(
        Guid crabId, DateTime? from, DateTime? to, int page = 1, int limit = 10, CancellationToken ct = default)
    {
        var end = (to ?? DateTime.UtcNow).ToUniversalTime();
        var start = (from ?? end.AddDays(-7)).ToUniversalTime();
        if (start >= end) throw AppException.BadRequest("'from' must be earlier than 'to'.");

        var span = end - start;
        var hourly = span.TotalHours <= 36;
        var granularity = hourly ? "hour" : "day";

        var crabKey = crabId.ToString();
        var all = (await _uow.FarmOperations.GetAllAsync(ct))
            .Where(o => IsFeedingOp(o) &&
                        o.CrabIdsJson.Contains(crabKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var current = all.Where(o => o.Timestamp >= start && o.Timestamp < end)
            .OrderByDescending(o => o.Timestamp)
            .ToList();
        var previous = all.Where(o => o.Timestamp >= start - span && o.Timestamp < start).ToList();

        // ---- summary ----
        var pcts = current.Select(FeedingPercentOf).Where(p => p.HasValue).Select(p => p!.Value).ToList();
        var prevPcts = previous.Select(FeedingPercentOf).Where(p => p.HasValue).Select(p => p!.Value).ToList();
        var acts = current.SelectMany(ActivityScoresOf).ToList();

        int? finishRate = pcts.Count == 0 ? null : (int)Math.Round(100.0 * pcts.Count(p => p >= FinishThreshold) / pcts.Count);
        int? prevFinishRate = prevPcts.Count == 0 ? null : (int)Math.Round(100.0 * prevPcts.Count(p => p >= FinishThreshold) / prevPcts.Count);
        int? avgPct = pcts.Count == 0 ? null : (int)Math.Round(pcts.Average());
        int? avgAct = acts.Count == 0 ? null : (int)Math.Round(acts.Average());

        var summary = new CrabFeedingActivitySummaryDto(
            FeedingCount: current.Count,
            FinishRate: finishRate,
            AvgFeedingPercent: avgPct,
            AvgActivityScore: avgAct,
            FeedingCountDelta: current.Count - previous.Count,
            FinishRateDelta: finishRate.HasValue && prevFinishRate.HasValue ? finishRate - prevFinishRate : null);

        // ---- trends (only buckets that have data; FE does not zero-fill) ----
        DateTime BucketOf(DateTime t) => hourly
            ? new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc)
            : new DateTime(t.Year, t.Month, t.Day, 0, 0, 0, DateTimeKind.Utc);

        var feedingTrend = current
            .GroupBy(o => BucketOf(o.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var gp = g.Select(FeedingPercentOf).Where(p => p.HasValue).Select(p => p!.Value).ToList();
                var served = g.Where(o => o.Quantity.HasValue).Sum(o => o.Quantity);
                var eaten = g.Where(o => o.EatenQuantity.HasValue).Sum(o => o.EatenQuantity);
                return new FeedingTrendPointDto(
                    g.Key,
                    gp.Count == 0 ? null : (int)Math.Round(gp.Average()),
                    g.Any(o => o.Quantity.HasValue) ? served : null,
                    g.Any(o => o.EatenQuantity.HasValue) ? eaten : null,
                    g.Count());
            })
            .ToList();

        var activityTrend = current
            .SelectMany(o => ActivityScoresOf(o).Select(s => (Bucket: BucketOf(o.Timestamp), Score: s)))
            .GroupBy(x => x.Bucket)
            .OrderBy(g => g.Key)
            .Select(g => new ActivityTrendPointDto(g.Key, (int)Math.Round(g.Average(x => x.Score)), g.Count()))
            .ToList();

        // ---- events page ----
        var pageSize = limit <= 0 ? 10 : Math.Min(limit, 200);
        var pageNum = page <= 0 ? 1 : page;
        var events = current.Skip((pageNum - 1) * pageSize).Take(pageSize).Select(Map).ToList();

        // ---- insight: only "anomaly detected" / "trend to watch" — never diagnoses ----
        var (insight, level) = BuildInsight(feedingTrend, avgAct, hourly);

        return ApiResponse<CrabFeedingActivityDto>.Ok(new CrabFeedingActivityDto(
            start, end, granularity, summary, feedingTrend, activityTrend, events, current.Count, insight, level));
    }

    private const int FinishThreshold = 80;

    private static bool IsFeedingOp(FarmOperation o) =>
        string.Equals(o.Type, "feeding", StringComparison.OrdinalIgnoreCase);

    /// <summary>feedingPercent = eaten / served × 100 (clamp 0–100); fallback appetite many/little/none → 100/40/0.</summary>
    private static int? FeedingPercentOf(FarmOperation o)
    {
        if (o.Quantity is > 0 && o.EatenQuantity.HasValue)
        {
            var pct = (double)(o.EatenQuantity.Value / o.Quantity.Value) * 100.0;
            return (int)Math.Round(Math.Clamp(pct, 0, 100));
        }
        return (o.Appetite ?? "").ToLowerInvariant() switch
        {
            "many" => 100,
            "little" => 40,
            "none" => 0,
            _ => null
        };
    }

    private static IEnumerable<int> ActivityScoresOf(FarmOperation o)
    {
        if (o.ActivityBefore.HasValue) yield return o.ActivityBefore.Value;
        if (o.ActivityAfter.HasValue) yield return o.ActivityAfter.Value;
    }

    private static int? ClampScore(int? v) => v.HasValue ? Math.Clamp(v.Value, 0, 100) : null;

    private static void ValidateFeedingAmounts(decimal? served, decimal? eaten)
    {
        if (eaten is < 0) throw AppException.BadRequest("Lượng đã ăn không được âm.");
        if (served.HasValue && eaten.HasValue && eaten > served)
            throw AppException.BadRequest("Lượng đã ăn không được lớn hơn khẩu phần.");
    }

    private static (string? Text, string Level) BuildInsight(
        IReadOnlyList<FeedingTrendPointDto> trend, int? avgActivity, bool hourly)
    {
        var pts = trend.Where(p => p.FeedingPercent.HasValue).ToList();
        if (pts.Count < 3) return (null, "none");

        var half = pts.Count / 2;
        var firstAvg = pts.Take(half).Average(p => p.FeedingPercent!.Value);
        var lastAvg = pts.Skip(pts.Count - half).Average(p => p.FeedingPercent!.Value);
        var lowStreak = 0;
        var maxLowStreak = 0;
        foreach (var p in pts)
        {
            lowStreak = p.FeedingPercent!.Value < 50 ? lowStreak + 1 : 0;
            maxLowStreak = Math.Max(maxLowStreak, lowStreak);
        }

        var unit = hourly ? "giờ" : "ngày";
        if (maxLowStreak >= 2 && lastAvg + 15 < firstAvg)
            return ($"AI phát hiện xu hướng giảm ăn trong {maxLowStreak} {unit} gần nhất. Khuyến nghị theo dõi thêm.", "warning");
        if (lastAvg + 15 < firstAvg)
            return ("AI phát hiện xu hướng mức ăn giảm so với đầu kỳ. Nên tiếp tục theo dõi.", "watch");
        if (avgActivity is < 30)
            return ("AI phát hiện mức vận động trung bình thấp trong kỳ. Nên tiếp tục theo dõi.", "watch");
        return ("AI nhận định: Mức ăn và vận động ổn định trong kỳ.", "ok");
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
