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
    private readonly FarmAreaScope _areas;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FarmOperationService(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<IEnumerable<FeedingHistoryDayDto>>> GetFeedingHistoryAsync(
        int days = 7, CancellationToken ct = default)
    {
        var end = DateTime.UtcNow.Date.AddDays(1);
        var start = end.AddDays(-(Math.Clamp(days, 1, 31)));
        var operations = await OperationsInScopeAsync(ct);
        var totals = Enumerable.Range(0, (end.Date - start.Date).Days)
            .Select(offset => start.Date.AddDays(offset))
            .ToDictionary(date => date, _ => new int[3]);

        // Một hộp / ngày: phiếu mới nhất. Cộng mọi phiếu thì BOX-0005 test làm phình "ăn nhiều".
        var latest = new Dictionary<(DateTime Day, string BoxId), (DateTime At, int Bucket)>();

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

            var boxIds = ParseStringList(operation.BoxIdsJson);
            if (boxIds.Count == 0)
                boxIds = ParseStringList(operation.CrabIdsJson);
            if (boxIds.Count == 0) continue;

            var day = operation.Timestamp.Date;
            if (!totals.ContainsKey(day)) continue;

            foreach (var boxId in boxIds)
            {
                var key = (day, boxId);
                if (!latest.TryGetValue(key, out var prev) || operation.Timestamp >= prev.At)
                    latest[key] = (operation.Timestamp, bucket);
            }
        }

        foreach (var item in latest)
            totals[item.Key.Day][item.Value.Bucket] += 1;

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
        var all = (await OperationsInScopeAsync(ct)).AsEnumerable();
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
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        var boxKey = boxId.ToString();
        var crabKeys = await OccupantCrabKeysAsync(boxId, ct);
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(o => MatchesBox(o, boxKey, crabKeys));
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

        var timestamp = req.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow;
        if (timestamp > DateTime.UtcNow.AddMinutes(5)) throw AppException.BadRequest("Thời điểm công việc không được ở tương lai.");
        if (req.FeedingDurationMinutes is < 0) throw AppException.BadRequest("Thời lượng cho ăn không được âm.");
        await EnsureRequestedBoxesAsync(req.BoxIds, ct);
        await EnsureRequestedCrabsAsync(req.CrabIds, ct);
        var crabIds = await ResolveCrabIdsAsync(req.CrabIds, req.BoxIds, timestamp, ct);
        await EnsureRequestedCrabsAsync(crabIds, ct);
        var boxIds = NormalizeIds(req.BoxIds);
        if (boxIds.Count == 0 && crabIds.Count == 0)
            throw AppException.BadRequest(
                "Farm operation phải gắn với ít nhất một box hoặc crab trong khu được phân công.");
        var actor = await _areas.CurrentUserAsync(ct)
            ?? throw AppException.Unauthorized();
        if (actor.Role != UserRole.FarmStaff) throw AppException.Forbidden("Chỉ Farm Staff ghi nhận công việc.");
        var condition = NormalizeConditionKey(req.Condition);
        ValidateFeedingAmounts(req.Quantity, req.EatenQuantity);
        await ValidateEvidenceAsync(req.PhotoUrls, boxIds, crabIds, ct);

        var op = new FarmOperation
        {
            Type = req.Type.Trim(),
            BoxIdsJson = JsonSerializer.Serialize(boxIds, JsonOpts),
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
            Timestamp = timestamp,
            OperatorId = actor.Id,
            OperatorName = string.IsNullOrWhiteSpace(actor.FullName)
                ? actor.Username
                : actor.FullName,
            Source = "manual",
            LocationLabel = req.LocationLabel
        };
        await _uow.FarmOperations.AddAsync(op, ct);

        // Đánh dấu tình trạng khi cho ăn → cập nhật luôn con cua, để hộp/hồ sơ/
        // cảnh báo thấy ngay. Ghi chung 1 SaveChanges với phiếu: hoặc cả hai, hoặc không.
        await ApplyConditionToCrabsAsync(crabIds, condition, op.Timestamp, actor.Id, ct);
        await TouchBoxesAsync(req.BoxIds, op.Timestamp, ct);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<FarmOperationDto>.Ok(Map(op), "Created.");
    }

    public async Task<ApiResponse<FarmOperationDto>> UpdateAsync(
        Guid id, UpdateFarmOperationRequest req, CancellationToken ct = default)
    {
        var op = await _uow.FarmOperations.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Farm operation");
        await EnsureOperationInScopeAsync(op, ct);
        var actor = await _areas.CurrentUserAsync(ct) ?? throw AppException.Unauthorized();
        if (actor.Role != UserRole.FarmStaff || op.OperatorId != actor.Id)
            throw AppException.Forbidden("Chỉ nhân viên tạo phiếu được sửa trong thời hạn cho phép.");
        if (op.Timestamp < DateTime.UtcNow.AddHours(-24))
            throw AppException.BadRequest("Operation can only be edited within 24 hours.");

        await EnsureRequestedBoxesAsync(req.BoxIds, ct);
        await EnsureRequestedCrabsAsync(NormalizeIds(req.CrabIds), ct);
        var proposedBoxIds = req.BoxIds is null
            ? ParseStringList(op.BoxIdsJson)
            : NormalizeIds(req.BoxIds);
        var proposedCrabIds = req.CrabIds is null
            ? ParseStringList(op.CrabIdsJson)
            : NormalizeIds(req.CrabIds);
        if (proposedBoxIds.Count == 0 && proposedCrabIds.Count == 0)
            throw AppException.BadRequest(
                "Farm operation phải gắn với ít nhất một box hoặc crab trong khu được phân công.");
        if (req.BoxIds is not null && !proposedBoxIds.SequenceEqual(ParseStringList(op.BoxIdsJson))
            || req.CrabIds is not null && !proposedCrabIds.SequenceEqual(ParseStringList(op.CrabIdsJson)))
            throw AppException.Conflict("Không đổi cua/hộp của phiếu đã ghi nhận; tạo phiếu bổ sung.");
        if (req.FeedingDurationMinutes is < 0) throw AppException.BadRequest("Thời lượng cho ăn không được âm.");
        await ValidateEvidenceAsync(req.PhotoUrls, proposedBoxIds, proposedCrabIds, ct);
        if (!string.IsNullOrWhiteSpace(req.Type)) op.Type = req.Type.Trim();
        if (req.BoxIds is not null)
            op.BoxIdsJson = JsonSerializer.Serialize(proposedBoxIds, JsonOpts);
        if (req.CrabIds is not null)
            op.CrabIdsJson = JsonSerializer.Serialize(proposedCrabIds, JsonOpts);
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
        await EnsureCrabInScopeAsync(crabId, ct);
        var allocs = (await _uow.CrabBoxAllocations.FindAsync(a => a.CrabId == crabId, ct)).ToList();
        var all = (await _uow.FarmOperations.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(o => MatchesCrab(o, crabId, allocs));
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
        await EnsureCrabInScopeAsync(crabId, ct);
        var end = (to ?? DateTime.UtcNow).ToUniversalTime();
        var start = (from ?? end.AddDays(-7)).ToUniversalTime();
        if (start >= end) throw AppException.BadRequest("'from' must be earlier than 'to'.");

        var span = end - start;
        var hourly = span.TotalHours <= 36;
        var granularity = hourly ? "hour" : "day";

        var allocs = (await _uow.CrabBoxAllocations.FindAsync(a => a.CrabId == crabId, ct)).ToList();
        var all = (await _uow.FarmOperations.GetAllAsync(ct))
            .Where(o => IsFeedingOp(o) && MatchesCrab(o, crabId, allocs))
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

    private async Task<IReadOnlyList<FarmOperation>> OperationsInScopeAsync(
        CancellationToken ct)
    {
        var operations = await _uow.FarmOperations.GetAllAsync(ct);
        var allowedAreas = await _areas.AreaIdsAsync(ct);
        if (allowedAreas is null)
            return operations.ToList();

        var rowIds = (await _uow.FarmingRows.GetAllAsync(ct))
            .Where(row => allowedAreas.Contains(row.FarmingAreaId))
            .Select(row => row.Id)
            .ToHashSet();
        var allowedBoxIds = (await _uow.Boxes.GetAllAsync(ct))
            .Where(box => rowIds.Contains(box.FarmingRowId))
            .Select(box => box.Id)
            .ToHashSet();
        var currentBoxes = (await _uow.Crabs.GetAllAsync(ct))
            .Where(crab => crab.BoxId.HasValue)
            .ToDictionary(crab => crab.Id, crab => crab.BoxId!.Value);
        var allocations = (await _uow.CrabBoxAllocations.GetAllAsync(ct))
            .GroupBy(item => item.CrabId)
            .ToDictionary(group => group.Key, group => group.ToList());
        bool CrabWasInScope(Guid crabId, DateTime at)
        {
            var boxAtOperation = allocations.GetValueOrDefault(crabId)?
                .Where(item => item.StartTime <= at
                    && (!item.EndTime.HasValue || item.EndTime.Value > at))
                .OrderByDescending(item => item.StartTime)
                .Select(item => (Guid?)item.BoxId)
                .FirstOrDefault();
            if (!boxAtOperation.HasValue
                && currentBoxes.TryGetValue(crabId, out var currentBoxId))
                boxAtOperation = currentBoxId;
            return boxAtOperation.HasValue
                && allowedBoxIds.Contains(boxAtOperation.Value);
        }

        return operations.Where(operation =>
        {
            var boxIds = ParseGuidSet(operation.BoxIdsJson);
            var crabIds = ParseGuidSet(operation.CrabIdsJson);
            if (boxIds.Count == 0 && crabIds.Count == 0)
                return false;
            return boxIds.All(allowedBoxIds.Contains)
                && crabIds.All(crabId => CrabWasInScope(crabId, operation.Timestamp));
        }).ToList();
    }

    private async Task EnsureOperationInScopeAsync(
        FarmOperation operation,
        CancellationToken ct)
    {
        if (!(await OperationsInScopeAsync(ct)).Any(item => item.Id == operation.Id))
            throw AppException.Forbidden("Không xác định được khu của bản ghi nghiệp vụ.");
    }

    private async Task EnsureRequestedBoxesAsync(
        IReadOnlyList<string>? rawIds,
        CancellationToken ct)
    {
        if (rawIds is null)
            return;
        foreach (var raw in rawIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(raw, out var boxId) || boxId == Guid.Empty)
                throw AppException.BadRequest($"BoxId '{raw}' không hợp lệ.");
            await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        }
    }

    private async Task EnsureRequestedCrabsAsync(
        IReadOnlyList<string>? rawIds,
        CancellationToken ct)
    {
        if (rawIds is null)
            return;
        foreach (var raw in rawIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(raw, out var crabId) || crabId == Guid.Empty)
                throw AppException.BadRequest($"CrabId '{raw}' không hợp lệ.");
            await EnsureCrabInScopeAsync(crabId, ct);
        }
    }

    private async Task EnsureCrabInScopeAsync(Guid crabId, CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
            ?? throw AppException.NotFound("Crab");
        if (crab.BoxId is Guid boxId && boxId != Guid.Empty)
        {
            await _areas.EnsureBoxAsync(boxId, manage: false, ct);
            return;
        }

        var allocation = (await _uow.CrabBoxAllocations.FindAsync(
                item => item.CrabId == crabId, ct))
            .OrderByDescending(item => item.StartTime)
            .FirstOrDefault();
        if (allocation is not null)
        {
            await _areas.EnsureBoxAsync(allocation.BoxId, manage: false, ct);
            return;
        }

        var lotAreaIds = (await _uow.CrabLotAreas.FindAsync(
                item => item.CrabLotId == crab.CrabLotId, ct))
            .Select(item => item.FarmingAreaId)
            .ToArray();
        await _areas.EnsureAllAssignedAsync(lotAreaIds, ct);
    }

    private static HashSet<Guid> ParseGuidSet(string? json) =>
        ParseStringList(json)
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

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

    private async Task ValidateEvidenceAsync(IReadOnlyList<string>? urls, IReadOnlyList<string> boxes, IReadOnlyList<string> crabs, CancellationToken ct)
    {
        if (urls is null) return;
        var boxIds = boxes.Select(Guid.Parse).ToHashSet();
        var crabIds = crabs.Select(Guid.Parse).ToHashSet();
        foreach (var url in urls)
        {
            MediaAsset? asset;
            if (MediaReferenceGuard.TryParseContentId(url, out var id)) asset = await _uow.MediaAssets.GetByIdAsync(id, ct);
            else asset = await _uow.MediaAssets.FirstOrDefaultAsync(m => m.ShareLink == url || m.WebViewLink == url || m.WebContentLink == url, ct);
            if (asset is null || !string.Equals(asset.Category, "image", StringComparison.OrdinalIgnoreCase)
                || (asset.CrabId.HasValue && !crabIds.Contains(asset.CrabId.Value))
                || (boxIds.Count > 0 && asset.BoxId.HasValue && !boxIds.Contains(asset.BoxId.Value))
                || !(asset.CrabId.HasValue && crabIds.Contains(asset.CrabId.Value) || asset.BoxId.HasValue && boxIds.Contains(asset.BoxId.Value)))
                throw AppException.Forbidden("Ảnh không thuộc cua/hộp của phiếu hoặc chưa đăng ký media.");
            if (asset.BoxId is Guid boxId) await _areas.EnsureBoxAsync(boxId, false, ct);
        }
    }

    private async Task TouchBoxesAsync(IEnumerable<string>? boxIds, DateTime at, CancellationToken ct)
    {
        if (boxIds is null) return;
        foreach (var raw in boxIds)
        {
            if (!Guid.TryParse(raw, out var boxId)) continue;
            var box = await _uow.Boxes.GetByIdAsync(boxId, ct);
            if (box is null) continue;
            box.UpdatedAt = at;
            _uow.Boxes.Update(box);
        }
    }

    /// <summary>
    /// Đồng bộ tình trạng của các cua trong phiếu + ghi CrabStatusHistory để
    /// hồ sơ cua / cảnh báo thấy được. Không đổi nếu tình trạng y hệt (tránh rác lịch sử).
    /// </summary>
    private async Task ApplyConditionToCrabsAsync(
        IReadOnlyList<string> crabIds, string? conditionKey, DateTime at, Guid actorId, CancellationToken ct)
    {
        var next = ConditionFromKey(conditionKey);
        if (next is null || crabIds.Count == 0) return;

        foreach (var raw in crabIds)
        {
            if (!Guid.TryParse(raw, out var crabId)) continue;
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct);
            if (crab is null || crab.Status is CrabStatus.Dead or CrabStatus.Harvested
                or CrabStatus.Sold or CrabStatus.Missing or CrabStatus.Quarantined) continue;
            if (at < (crab.UpdatedAt ?? crab.CreatedAt) || at < crab.StockedAt) continue;

            var oldCondition = crab.Condition;
            var oldStatus = crab.Status;
            if (oldCondition == next.Value) continue;

            crab.Condition = next.Value;
            crab.Status = CrabConditions.ApplyCondition(oldStatus, next.Value);
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
                ChangedByUserId = actorId,
                Reason = "Ghi nhận khi cho ăn"
            }, ct);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveCrabIdsAsync(
        IReadOnlyList<string>? requested, IReadOnlyList<string>? boxIds, DateTime at, CancellationToken ct)
    {
        var crabIds = NormalizeIds(requested);
        if (crabIds.Count > 0 || boxIds is null || boxIds.Count == 0)
            return crabIds;

        var filled = new List<string>();
        foreach (var raw in boxIds)
        {
            if (!Guid.TryParse(raw, out var boxId)) continue;
            var occupantIds = at < DateTime.UtcNow.AddMinutes(-5)
                ? (await _uow.CrabBoxAllocations.FindAsync(a => a.BoxId == boxId && a.StartTime <= at && (a.EndTime == null || a.EndTime > at), ct)).Select(a => a.CrabId)
                : (await _uow.Crabs.FindAsync(c => c.BoxId == boxId, ct)).Select(c => c.Id);
            foreach (var crabId in occupantIds)
            {
                var id = crabId.ToString();
                if (!filled.Contains(id, StringComparer.OrdinalIgnoreCase))
                    filled.Add(id);
            }
        }
        return filled;
    }

    private async Task<HashSet<string>> OccupantCrabKeysAsync(Guid boxId, CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var occupants = await _uow.Crabs.FindAsync(c => c.BoxId == boxId, ct);
        foreach (var crab in occupants)
            keys.Add(crab.Id.ToString());
        return keys;
    }

    private static bool MatchesBox(FarmOperation o, string boxKey, HashSet<string> crabKeys)
    {
        if (o.BoxIdsJson.Contains(boxKey, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var key in crabKeys)
        {
            if (o.CrabIdsJson.Contains(key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool MatchesCrab(
        FarmOperation o, Guid crabId, IReadOnlyList<CrabBoxAllocation> allocs)
    {
        var crabKey = crabId.ToString();
        if (o.CrabIdsJson.Contains(crabKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (ParseStringList(o.CrabIdsJson).Count > 0)
            return false;

        var boxes = ParseStringList(o.BoxIdsJson);
        if (boxes.Count == 0) return false;

        foreach (var alloc in allocs)
        {
            if (alloc.StartTime > o.Timestamp) continue;
            if (alloc.EndTime is DateTime end && end <= o.Timestamp) continue;
            var allocBox = alloc.BoxId.ToString();
            if (boxes.Any(b => string.Equals(b, allocBox, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
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
