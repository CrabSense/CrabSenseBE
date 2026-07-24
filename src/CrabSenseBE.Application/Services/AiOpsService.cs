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

        var detection = new AiDetection
        {
            BoxId = boxId,
            MediaId = media?.Id,
            DeviceId = media?.DeviceId,
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
        return ApiResponse<AiDetectionDto>.Ok(Map(detection), "Analyzed.");
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
    {
        var q = (await _uow.AiDetections.GetAllAsync(ct)).AsEnumerable();
        if (boxId.HasValue)
            q = q.Where(d => d.BoxId == boxId.Value);
        if (mediaId.HasValue)
            q = q.Where(d => d.MediaId == mediaId.Value);
        var list = q.OrderByDescending(d => d.DetectedAt).Select(Map).ToList();
        return ApiResponse<IEnumerable<AiDetectionDto>>.Ok(list);
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

    private static AiDetectionDto Map(AiDetection d) => new(
        d.Id, d.BoxId, d.MediaId, d.DetectionType, d.Confidence,
        d.Status, d.ResultJson, d.DetectedAt, d.ModelVersion);
}
