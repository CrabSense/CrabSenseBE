using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class AiOpsService : IAiOpsService
{
    private readonly IUnitOfWork _uow;

    public AiOpsService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<AiDetectionDto>> AnalyzeAsync(
        AiAnalyzeRequest req, CancellationToken ct = default)
    {
        var mediaId = req.MediaId ?? req.VideoId;
        MediaAsset? media = null;
        if (mediaId is Guid mid && mid != Guid.Empty)
            media = await _uow.MediaAssets.GetByIdAsync(mid, ct);

        var boxId = req.BoxId ?? media?.BoxId;
        Box? box = null;
        if (boxId is Guid bid && bid != Guid.Empty)
            box = await _uow.Boxes.GetByIdAsync(bid, ct);

        // Build recommendation from live farm signals (not hardcoded stub copy).
        var (detectionType, confidence, note, health) =
            await BuildHeuristicAsync(box, boxId, ct);

        // Camera ghi nhận: theo media; nếu không có → camera gắn dãy / camera tổng quan khu của hộp.
        FarmingRow? row = box is not null ? await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct) : null;
        var deviceId = media?.DeviceId;
        if (deviceId is null && row is not null)
            deviceId = PickCamera(await _uow.Devices.GetAllAsync(ct), row)?.Id;
        // Cua đang ở trong hộp tại thời điểm phân tích.
        Guid? crabId = null;
        if (boxId is Guid bx)
            crabId = (await _uow.Crabs.FindAsync(c => c.BoxId == bx, ct))
                .Where(c => c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined)
                .OrderByDescending(c => c.StockedAt)
                .FirstOrDefault()?.Id;

        var detection = new AiDetection
        {
            CrabId = crabId,
            BoxId = boxId,
            MediaId = media?.Id,
            DeviceId = deviceId,
            ModelVersion = "crabsense-ai-v1-rules",
            DetectionType = detectionType,
            Confidence = confidence,
            Status = "completed",
            DetectedAt = DateTime.UtcNow,
            ImagePath = media?.WebViewLink ?? media?.StorageKey,
            ResultJson = JsonSerializer.Serialize(new
            {
                moltingLikely = detectionType.Contains("molt", StringComparison.OrdinalIgnoreCase),
                health,
                note,
                boxId,
                mediaId = media?.Id,
                source = "rules-engine"
            })
        };

        await _uow.AiDetections.AddAsync(detection, ct);
        await _uow.SaveChangesAsync(ct);

        Device? dev = deviceId is Guid did ? await _uow.Devices.GetByIdAsync(did, ct) : null;
        Crab? crab = crabId is Guid cid ? await _uow.Crabs.GetByIdAsync(cid, ct) : null;
        return ApiResponse<AiDetectionDto>.Ok(Map(detection, dev, box, row, crab), "Analyzed.");
    }

    private async Task<(string type, decimal confidence, string note, string health)> BuildHeuristicAsync(
        Box? box, Guid? boxId, CancellationToken ct)
    {
        var detectionType = "health_check";
        var confidence = 0.72m;
        var health = "normal";
        var notes = new List<string>();

        if (box is not null)
        {
            var status = (box.Status ?? "").ToLowerInvariant();
            if (status is "molting")
            {
                detectionType = "molting";
                confidence = 0.88m;
                health = "stress";
                notes.Add($"Box {box.Code} đang ở trạng thái lột — kiểm tra softshell trước thu hoạch.");
            }
            else if (status is "quarantine")
            {
                detectionType = "quarantine_risk";
                confidence = 0.91m;
                health = "disease";
                notes.Add($"Box {box.Code} đang cách ly — ưu tiên kiểm tra sức khỏe cua.");
            }
            else if (status is "active" || box.IsOccupied)
            {
                detectionType = "occupancy_ok";
                confidence = 0.80m;
                notes.Add($"Box {box.Code} đang nuôi — theo dõi nước và lột định kỳ.");
            }
        }

        if (boxId is Guid bid && bid != Guid.Empty)
        {
            var crabs = (await _uow.Crabs.FindAsync(c => c.BoxId == bid, ct)).ToList();
            var live = crabs.Count(c =>
                c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined);
            if (live == 0)
            {
                detectionType = "empty_box";
                confidence = 0.86m;
                notes.Add("Không có cua sống trong hộp — xác nhận lại trước khi ghi nhận AI.");
            }
            else if (crabs.Any(c =>
                         string.Equals(c.MoltingStage, "softShell", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(c.MoltingStage, "softshell", StringComparison.OrdinalIgnoreCase)))
            {
                detectionType = "softshell";
                confidence = 0.84m;
                health = "stress";
                notes.Add("Phát hiện giai đoạn softshell — cân nhắc cửa sổ thu hoạch.");
            }
        }

        // Water / alert pressure from farming area
        if (box is not null)
        {
            var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
            if (row is not null)
            {
                var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == row.FarmingAreaId, ct))
                    .Select(w => w.Id).ToHashSet();
                var sensorIds = (await _uow.Sensors.FindAsync(
                        s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value), ct))
                    .Select(s => s.Id).ToHashSet();
                var alertCount = (await _uow.Alerts.FindAsync(
                        a => a.Status == AlertStatus.Active && a.SensorId != null &&
                             sensorIds.Contains(a.SensorId.Value), ct))
                    .Count();
                if (alertCount > 0)
                {
                    confidence = Math.Min(0.95m, confidence + 0.05m);
                    health = health == "normal" ? "warning" : health;
                    notes.Add($"Có {alertCount} cảnh báo nước/sensor đang active — nên kiểm tra chất lượng nước.");
                    if (detectionType is "occupancy_ok" or "health_check")
                        detectionType = "water_alert";
                }
            }
        }

        if (notes.Count == 0)
            notes.Add("Phân tích dựa trên trạng thái hộp và tín hiệu trại hiện có.");

        return (detectionType, confidence, string.Join(" ", notes), health);
    }

    public async Task<ApiResponse<IEnumerable<AiDetectionDto>>> ListDetectionsAsync(
        Guid? boxId = null, Guid? mediaId = null, CancellationToken ct = default)
        => await ListDetectionsAsync(boxId, mediaId, null, null, ct);

    public async Task<ApiResponse<IEnumerable<AiDetectionDto>>> ListDetectionsAsync(
        Guid? boxId, Guid? mediaId, Guid? farmingAreaId, int? take, CancellationToken ct = default)
    {
        var q = (await _uow.AiDetections.GetAllAsync(ct)).AsEnumerable();
        if (boxId.HasValue)
            q = q.Where(d => d.BoxId == boxId.Value);
        if (mediaId.HasValue)
            q = q.Where(d => d.MediaId == mediaId.Value);

        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToDictionary(b => b.Id);
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToDictionary(r => r.Id);
        var devices = (await _uow.Devices.GetAllAsync(ct)).ToDictionary(d => d.Id);
        // Cua đang ở trong hộp: để ghép mã cua cho phát hiện theo hộp (nếu detection không có CrabId).
        var crabInBox = (await _uow.Crabs.GetAllAsync(ct))
            .Where(c => c.BoxId != null && c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined)
            .GroupBy(c => c.BoxId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.StockedAt).First());
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).ToDictionary(c => c.Id);

        Guid? AreaOf(AiDetection d)
        {
            if (d.BoxId is Guid bid && boxes.TryGetValue(bid, out var b)
                && rows.TryGetValue(b.FarmingRowId, out var r))
                return r.FarmingAreaId;
            if (d.DeviceId is Guid did && devices.TryGetValue(did, out var dev))
                return dev.FarmingAreaId;
            return null;
        }

        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
            q = q.Where(d => AreaOf(d) == areaId);

        IEnumerable<AiDetection> ordered = q.OrderByDescending(d => d.DetectedAt);
        if (take is > 0) ordered = ordered.Take(take.Value);

        var list = ordered.Select(d =>
        {
            boxes.TryGetValue(d.BoxId ?? Guid.Empty, out var box);
            FarmingRow? row = null;
            if (box is not null) rows.TryGetValue(box.FarmingRowId, out row);
            devices.TryGetValue(d.DeviceId ?? Guid.Empty, out var dev);
            // Camera phụ trách: DeviceId của detection → camera gắn dãy → camera tổng quan khu.
            dev ??= PickCamera(devices.Values, row);
            Crab? crab = null;
            if (d.CrabId is Guid cid) crabs.TryGetValue(cid, out crab);
            if (crab is null && box is not null) crabInBox.TryGetValue(box.Id, out crab);
            return Map(d, dev, box, row, crab);
        }).ToList();
        return ApiResponse<IEnumerable<AiDetectionDto>>.Ok(list);
    }

    private static Device? PickCamera(IEnumerable<Device> devices, FarmingRow? row)
    {
        var cams = devices
            .Where(d => (d.DeviceType ?? "").Contains("cam", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (row is null) return null;
        return cams.FirstOrDefault(c => c.FarmingRowId == row.Id)
            ?? cams.Where(c => c.FarmingAreaId == row.FarmingAreaId && c.FarmingRowId == null)
                .OrderByDescending(c => c.LastSeenAt)
                .FirstOrDefault()
            ?? cams.FirstOrDefault(c => c.FarmingAreaId == row.FarmingAreaId);
    }

    public async Task<ApiResponse<object>> SubmitFeedbackAsync(
        AiFeedbackRequest req, Guid userId, CancellationToken ct = default)
    {
        var detectionId = req.AiDetectionId ?? req.DetectionId;
        if (detectionId is null || detectionId == Guid.Empty)
            throw AppException.BadRequest("AiDetectionId is required.");

        _ = await _uow.AiDetections.GetByIdAsync(detectionId.Value, ct)
            ?? throw AppException.NotFound("AI detection");

        var feedback = new AiFeedback
        {
            AiDetectionId = detectionId.Value,
            UserId = userId == Guid.Empty ? Guid.NewGuid() : userId,
            IsCorrect = req.AiAgreement ?? req.IsCorrect,
            CorrectLabel = req.CorrectLabel,
            Comment = req.Comment
        };
        await _uow.AiFeedbacks.AddAsync(feedback, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<object>.Ok(new { id = feedback.Id }, "Feedback saved.");
    }

    private static AiDetectionDto Map(
        AiDetection d, Device? dev = null, Box? box = null, FarmingRow? row = null, Crab? crab = null) => new(
        d.Id, d.BoxId, d.MediaId, d.DetectionType, d.Confidence,
        d.Status, d.ResultJson, d.DetectedAt, d.ModelVersion,
        dev?.Id ?? d.DeviceId, dev?.DeviceCode, d.ImagePath,
        box?.Code,
        crab?.Id ?? d.CrabId, crab is null ? null : (string.IsNullOrWhiteSpace(crab.Tag) ? crab.Code : crab.Tag),
        row?.Id, row?.Name, row?.FarmingAreaId);
}
