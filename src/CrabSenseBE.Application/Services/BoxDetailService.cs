using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class BoxDetailService : IBoxDetailService
{
    private readonly IUnitOfWork _uow;
    private readonly IBoxQrService _boxQr;

    public BoxDetailService(IUnitOfWork uow, IBoxQrService boxQr)
    {
        _uow = uow;
        _boxQr = boxQr;
    }

    public async Task<ApiResponse<BoxDetailDto>> GetDetailAsync(Guid boxId, CancellationToken ct = default)
    {
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");
        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        FarmingArea? area = null;
        if (row is not null)
            area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);

        var crabs = (await _uow.Crabs.FindAsync(c => c.BoxId == boxId, ct)).ToList();
        var alive = crabs.Where(c =>
            c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined).ToList();
        var avgWeight = alive.Count == 0
            ? 0m
            : alive.Where(c => c.WeightGram.HasValue).Select(c => c.WeightGram!.Value).DefaultIfEmpty(0).Average();

        string qrCode = box.Code;
        try
        {
            var qr = await _boxQr.EnsureBoxQrAsync(boxId, ct);
            if (qr.Success && qr.Data is not null)
                qrCode = qr.Data.Code;
        }
        catch
        {
            // keep box.Code
        }

        var lastVideo = (await _uow.MediaAssets.FindAsync(
                m => m.BoxId == boxId && m.Category == "video", ct))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        var areaId = row?.FarmingAreaId ?? Guid.Empty;
        return ApiResponse<BoxDetailDto>.Ok(new BoxDetailDto(
            box.Id,
            box.FarmingRowId,
            areaId,
            row?.Name,
            area?.Name,
            box.Code,
            box.Status,
            box.IsOccupied,
            QrCode: qrCode,
            FarmId: areaId,
            PondId: box.FarmingRowId,
            Location: new BoxLocationDto(0, 0, area?.Name ?? row?.Name ?? box.Code),
            CurrentCrabCount: alive.Count,
            Capacity: row?.Capacity > 0 ? row.Capacity : Math.Max(alive.Count, 1),
            Species: "mudCrab",
            AverageWeight: Math.Round(avgWeight, 1),
            CreatedAt: box.CreatedAt,
            LastVideoAt: lastVideo?.CreatedAt));
    }

    public async Task<ApiResponse<IEnumerable<BoxCrabItemDto>>> GetCrabsAsync(
        Guid boxId, CancellationToken ct = default)
    {
        _ = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");
        var crabs = (await _uow.Crabs.FindAsync(c => c.BoxId == boxId, ct)).ToList();
        var list = crabs.Select(c =>
        {
            var health = c.Status switch
            {
                CrabStatus.Quarantined => "disease",
                CrabStatus.Dead => "unknown",
                CrabStatus.Molting => "stress",
                _ => "normal"
            };
            var molt = string.IsNullOrWhiteSpace(c.MoltingStage) ? "hardShell" : c.MoltingStage;
            return new BoxCrabItemDto(
                c.Id,
                boxId,
                Species: "mudCrab",
                Weight: c.WeightGram ?? 0,
                MoltingStatus: molt,
                HealthStatus: health,
                Source: "farm",
                AddedAt: c.StockedAt == default ? c.CreatedAt : c.StockedAt,
                AddedBy: "system",
                WeightGram: c.WeightGram,
                MoltingStage: c.MoltingStage,
                Tag: c.Tag,
                ImageUrls: JsonStringList.Parse(c.ImageUrlsJson));
        });
        return ApiResponse<IEnumerable<BoxCrabItemDto>>.Ok(list);
    }

    public async Task<ApiResponse<IEnumerable<BoxVideoItemDto>>> GetVideosAsync(
        Guid boxId, CancellationToken ct = default)
    {
        _ = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");
        var media = (await _uow.MediaAssets.FindAsync(
                m => m.BoxId == boxId && m.Category == "video", ct))
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        var list = media.Select(m => new BoxVideoItemDto(
            m.Id,
            boxId,
            LocalPath: m.WebViewLink ?? m.ShareLink ?? m.StorageKey,
            DurationSeconds: 0,
            FileSizeBytes: m.SizeBytes,
            Status: "uploaded",
            CapturedAt: m.CreatedAt,
            CapturedBy: m.UploadedBy?.ToString() ?? "",
            UploadedAt: m.CreatedAt,
            AiDetectionId: null,
            RetryCount: 0));
        return ApiResponse<IEnumerable<BoxVideoItemDto>>.Ok(list);
    }

    public async Task<ApiResponse<object>> ResolveQrAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("QR code is required.");

        var scan = await _boxQr.ScanAsync(code.Trim(), ct);
        if (!scan.Success || scan.Data?.Box is null)
            throw AppException.NotFound("Box for QR code");

        return ApiResponse<object>.Ok(new { boxId = scan.Data.Box.Id.ToString() });
    }

    public async Task<ApiResponse<BoxQrQuickResultDto>> GetQuickResultByQrAsync(
        string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw AppException.BadRequest("QR code is required.");

        var raw = code.Trim();
        // Strip mobile sticker prefix if present.
        var lookup = raw.StartsWith("CRABSENSE:BOX:", StringComparison.OrdinalIgnoreCase)
            ? raw["CRABSENSE:BOX:".Length..]
            : raw;

        var scan = await _boxQr.ScanAsync(lookup, ct);
        if (!scan.Success || scan.Data?.Box is null)
            throw AppException.NotFound("Box for QR code");

        var scanned = scan.Data;
        var boxId = scanned.Box.Id;
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct)
            ?? throw AppException.NotFound("Box");

        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        FarmingArea? area = null;
        if (row is not null)
            area = await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);

        var farmId = area?.Id ?? row?.FarmingAreaId ?? Guid.Empty;
        var farmName = !string.IsNullOrWhiteSpace(scanned.AreaName)
            ? scanned.AreaName
            : (area?.Name ?? "—");
        var rowName = scanned.RowName;

        var crabCount = scanned.Crabs.Count;

        // Water live for farming area
        decimal? temperature = null;
        decimal? ph = null;
        DateTime? waterAt = null;
        var waterAlarms = 0;
        if (farmId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == farmId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var sensors = (await _uow.Sensors.FindAsync(
                    s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value), ct))
                .ToList();
            var allMeas = await _uow.WaterMeasurements.GetAllAsync(ct);
            var latestBySensor = allMeas
                .GroupBy(m => m.SensorId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasuredAt).First());

            foreach (var s in sensors)
            {
                if (!latestBySensor.TryGetValue(s.Id, out var latest)) continue;
                var type = s.SensorType.ToLowerInvariant();
                if (type.Contains("temp") && temperature is null)
                {
                    temperature = latest.Value;
                    waterAt = latest.MeasuredAt;
                }
                if ((type.Contains("ph") || type == "ph") && ph is null)
                {
                    ph = latest.Value;
                    waterAt ??= latest.MeasuredAt;
                }
                if (s.MinThreshold.HasValue && latest.Value < s.MinThreshold.Value) waterAlarms++;
                if (s.MaxThreshold.HasValue && latest.Value > s.MaxThreshold.Value) waterAlarms++;
            }
        }

        // Alerts for farming area
        var alertDtos = new List<BoxQrQuickAlertDto>();
        IEnumerable<Alert> areaAlerts = Array.Empty<Alert>();
        if (farmId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == farmId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var sensorIds = (await _uow.Sensors.FindAsync(
                    s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value), ct))
                .Select(s => s.Id)
                .ToHashSet();
            areaAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct))
                .Where(a => a.SensorId is Guid sid && sensorIds.Contains(sid))
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            foreach (var a in areaAlerts.Take(5))
            {
                alertDtos.Add(new BoxQrQuickAlertDto(
                    string.IsNullOrWhiteSpace(a.Message) ? "Cảnh báo" : a.Message,
                    a.Severity.ToString().ToLowerInvariant()));
            }
        }

        // Devices used only for health weighting.
        var devices = (await _uow.Devices.GetAllAsync(ct)).ToList();

        // AI detections for this box only — no invented recommendation text.
        string? aiRec = null;
        double? aiConf = null;
        var aiScore = 0;
        var detections = (await _uow.AiDetections.FindAsync(d => d.BoxId == boxId, ct))
            .OrderByDescending(d => d.DetectedAt)
            .ToList();
        if (detections.Count > 0)
        {
            var first = detections[0];
            aiConf = (double)first.Confidence;
            if (aiConf <= 1) aiConf *= 100;
            aiScore = Math.Clamp((int)Math.Round(aiConf.Value), 0, 100);

            aiRec = TryReadAiNote(first.ResultJson);
            if (string.IsNullOrWhiteSpace(aiRec) && !string.IsNullOrWhiteSpace(first.DetectionType))
                aiRec = $"Phát hiện {first.DetectionType}";
        }

        // Health score from real box status + water + devices + alert count
        var alertCount = areaAlerts.Count();
        var status = box.Status ?? "";
        var crabScore = 85;
        if (string.Equals(status, "quarantine", StringComparison.OrdinalIgnoreCase)) crabScore = 35;
        else if (string.Equals(status, "maintenance", StringComparison.OrdinalIgnoreCase)) crabScore = 55;
        else if (string.Equals(status, "empty", StringComparison.OrdinalIgnoreCase) && !box.IsOccupied) crabScore = 90;
        else if (string.Equals(status, "molting", StringComparison.OrdinalIgnoreCase)) crabScore = 78;
        else if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) || box.IsOccupied) crabScore = 92;
        crabScore = Math.Clamp(crabScore - Math.Min(alertCount, 5) * 3, 0, 100);

        var waterScore = 82;
        if (waterAlarms > 0) waterScore = Math.Clamp(waterScore - waterAlarms * 8, 0, 100);
        if (ph is not null && (ph < 6.5m || ph > 8.5m)) waterScore = Math.Clamp(waterScore - 10, 0, 100);
        if (temperature is not null && (temperature < 24m || temperature > 32m))
            waterScore = Math.Clamp(waterScore - 10, 0, 100);

        var deviceScore = devices.Count == 0
            ? 80
            : (int)Math.Round(devices.Count(d => d.Status == DeviceStatus.Online) * 100.0 / devices.Count);

        var healthScore = (int)Math.Round(waterScore * 0.35 + crabScore * 0.40 + deviceScore * 0.25);
        healthScore = Math.Clamp(healthScore, 0, 100);

        var healthStatus = healthScore switch
        {
            >= 85 => "Healthy",
            >= 70 => "Good",
            >= 50 => "Warning",
            _ => "Critical"
        };
        if (string.Equals(status, "quarantine", StringComparison.OrdinalIgnoreCase))
            healthStatus = "Critical";
        else if (string.Equals(status, "maintenance", StringComparison.OrdinalIgnoreCase))
            healthStatus = "Offline";

        var lastVideo = (await _uow.MediaAssets.FindAsync(
                m => m.BoxId == boxId && m.Category == "video", ct))
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault();

        var dto = new BoxQrQuickResultDto(
            BoxId: boxId,
            Code: scanned.Box.Code,
            RawValue: raw,
            FarmId: farmId,
            FarmName: farmName,
            RowName: rowName,
            StatusLabel: healthStatus,
            HealthStatus: healthStatus.ToLowerInvariant(),
            HealthScore: healthScore,
            AiScore: aiScore,
            CrabCount: crabCount,
            Temperature: temperature,
            Ph: ph,
            UpdatedAt: waterAt ?? lastVideo?.CreatedAt ?? box.UpdatedAt,
            AiRecommendation: string.IsNullOrWhiteSpace(aiRec) ? null : aiRec,
            AiConfidence: aiConf,
            Alerts: alertDtos);

        return ApiResponse<BoxQrQuickResultDto>.Ok(dto);
    }

    private static string? TryReadAiNote(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (doc.RootElement.TryGetProperty("note", out var noteProp))
            {
                var note = noteProp.GetString();
                // Skip known AI stub placeholder copy.
                if (string.IsNullOrWhiteSpace(note)) return null;
                if (note.Contains("Heuristic analysis", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (note.Contains("replace with real AI", StringComparison.OrdinalIgnoreCase))
                    return null;
                return note.Trim();
            }
        }
        catch (JsonException)
        {
            // ignore malformed JSON
        }
        return null;
    }

    public async Task<ApiResponse<BoxCrabItemDto>> AddCrabAsync(
        Guid boxId, MobileAddCrabRequest req, CancellationToken ct = default)
    {
        var box = await _uow.Boxes.GetByIdAsync(boxId, ct) ?? throw AppException.NotFound("Box");

        var liveInBox = (await _uow.Crabs.FindAsync(c => c.BoxId == boxId, ct))
            .Any(c => c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined);
        if (liveInBox)
            throw AppException.Conflict($"Box '{box.Code}' already has a live crab.");

        Guid crabLotId;
        if (req.CrabLotId is Guid lotId && lotId != Guid.Empty)
        {
            _ = await _uow.CrabLots.GetByIdAsync(lotId, ct) ?? throw AppException.NotFound("CrabLot");
            crabLotId = lotId;
        }
        else
        {
            var anyLot = (await _uow.CrabLots.GetAllAsync(ct)).OrderBy(l => l.CreatedAt).FirstOrDefault()
                ?? throw AppException.BadRequest("No CrabLot available — create a crab lot first, or pass crabLotId.");
            crabLotId = anyLot.Id;
        }

        var weight = req.WeightGram ?? req.Weight;
        var molt = req.MoltingStage ?? req.MoltingStatus ?? "hardShell";

        // Explicit owner stance wins; otherwise derive from molting stage — same rule as desktop CreateCrabAsync.
        var condition = string.IsNullOrWhiteSpace(req.Condition)
            ? CrabConditions.FromMoltingAndStatus(molt, CrabStatus.Alive)
            : CrabConditions.Parse(req.Condition);

        // Code có unique index và không được rỗng: thiếu bước này thì lần thả thứ hai trở đi
        // vi phạm IX_Crabs_Code (Code = "") và trả 500. Mirror desktop CreateCrabAsync.
        var code = await CrabCodeAllocator.AllocateAsync(_uow.Crabs, ct);

        var crab = new Crab
        {
            BoxId = boxId,
            CrabLotId = crabLotId,
            Code = code,
            QrCode = $"QR-{code}",
            Tag = string.IsNullOrWhiteSpace(req.Tag) ? code : req.Tag.Trim(),
            CrabType = string.IsNullOrWhiteSpace(req.CrabType) ? null : req.CrabType.Trim(),
            Gender = CrabConditions.ParseGender(req.Gender),
            WeightGram = weight,
            InitialWeightGram = weight,
            CarapaceLengthMm = req.CarapaceLengthMm,
            CarapaceWidthMm = req.CarapaceWidthMm,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            MoltingStage = molt,
            StockedAt = DateTime.UtcNow,
            Condition = condition,
            Status = CrabConditions.ToLifecycle(condition),
            ImageUrlsJson = JsonStringList.Serialize(req.ImageUrls)
        };
        await _uow.Crabs.AddAsync(crab, ct);

        // Tem QR cấp cho cá thể — desktop làm ở CreateCrabAsync; thiếu thì tem có mã nhưng quét không ra.
        await _uow.QrCodes.AddAsync(new QrCode
        {
            Code = crab.QrCode!,
            EntityType = "crab",
            CrabId = crab.Id,
            BoxId = boxId,
            IsActive = true,
            Payload =
                $"{{\"type\":\"crab\",\"crabId\":\"{crab.Id}\",\"crabCode\":\"{code}\",\"crabsense\":\"CRABSENSE:CRAB:{code}\"}}"
        }, ct);

        await _uow.CrabBoxAllocations.AddAsync(new CrabBoxAllocation
        {
            CrabId = crab.Id,
            BoxId = boxId,
            StartTime = DateTime.UtcNow,
            Notes = "Mobile add crab"
        }, ct);

        box.IsOccupied = true;
        if (string.IsNullOrWhiteSpace(box.Status) || box.Status == "empty")
            box.Status = "active";
        _uow.Boxes.Update(box);

        var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
        var area = row is null
            ? null
            : await _uow.FarmingAreas.GetByIdAsync(row.FarmingAreaId, ct);
        if (area is not null)
        {
            await _uow.OperationLogs.AddAsync(new OperationLog
            {
                UserId = area.OwnerId,
                Action = "crab_added",
                EntityType = "Crab",
                EntityId = crab.Id,
                Details = $"Thêm cua {crab.Code} vào hộp {box.Code}"
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);

        return ApiResponse<BoxCrabItemDto>.Ok(new BoxCrabItemDto(
            crab.Id,
            boxId,
            Species: string.IsNullOrWhiteSpace(req.Species) ? "mudCrab" : req.Species!,
            Weight: weight ?? 0,
            MoltingStatus: molt,
            HealthStatus: "normal",
            Source: "farm",
            AddedAt: crab.StockedAt,
            AddedBy: "mobile",
            WeightGram: weight,
            MoltingStage: molt,
            Tag: crab.Tag,
            ImageUrls: JsonStringList.Parse(crab.ImageUrlsJson)));
    }
}
