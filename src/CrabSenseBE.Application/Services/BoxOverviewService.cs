using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Aggregates farming status + area IoT + alerts into one Mobile Boxes payload.
/// Water/devices are area-scoped (RAS); per-box score blends box crab status with area IoT.
/// </summary>
public class BoxOverviewService : IBoxOverviewService
{
    private readonly IUnitOfWork _uow;

    public BoxOverviewService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<BoxesOverviewResponseDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        Guid? farmingRowId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToList();
        var rowMap = rows.ToDictionary(r => r.Id);
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);

        var boxes = (await _uow.Boxes.GetAllAsync(ct)).AsEnumerable();
        if (farmingRowId.HasValue)
            boxes = boxes.Where(b => b.FarmingRowId == farmingRowId.Value);
        if (farmingAreaId.HasValue && farmingAreaId != Guid.Empty)
        {
            var rowIds = rows.Where(r => r.FarmingAreaId == farmingAreaId.Value).Select(r => r.Id).ToHashSet();
            boxes = boxes.Where(b => rowIds.Contains(b.FarmingRowId));
        }
        if (!string.IsNullOrWhiteSpace(status))
            boxes = boxes.Where(b => string.Equals(b.Status, status, StringComparison.OrdinalIgnoreCase));

        var boxList = boxes.OrderBy(b => b.Code).ToList();
        var boxIds = boxList.Select(b => b.Id).ToHashSet();

        // Resolve primary area for IoT (first box's area, or filter)
        Guid? scopeAreaId = farmingAreaId;
        if (scopeAreaId is null || scopeAreaId == Guid.Empty)
        {
            if (boxList.Count > 0 && rowMap.TryGetValue(boxList[0].FarmingRowId, out var firstRow))
                scopeAreaId = firstRow.FarmingAreaId;
        }

        string farmName = "Trang trại";
        if (scopeAreaId is Guid aid && areas.TryGetValue(aid, out var areaEntity))
            farmName = areaEntity.Name;

        var (waterSystems, sensors, devices, measurements, alerts) =
            await LoadAreaIoTAsync(scopeAreaId, ct);

        var water = BuildWaterSnapshot(sensors, measurements);
        var deviceSnap = BuildDeviceSnapshot(devices);
        var (waterScore, deviceScore) = ComputeAreaScores(sensors, devices, alerts);

        // Crabs currently in these boxes
        var crabs = (await _uow.Crabs.GetAllAsync(ct))
            .Where(c => c.BoxId is Guid bid && boxIds.Contains(bid))
            .ToList();
        var crabsByBox = crabs.GroupBy(c => c.BoxId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var lots = (await _uow.CrabLots.GetAllAsync(ct)).ToDictionary(l => l.Id);

        // Open allocations for molt / harvest hints
        var openAllocs = (await _uow.CrabBoxAllocations.FindAsync(
                a => a.EndTime == null && boxIds.Contains(a.BoxId), ct))
            .GroupBy(a => a.BoxId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartTime).First());

        var molts = (await _uow.MoltingRecords.FindAsync(
                m => m.BoxId != null && boxIds.Contains(m.BoxId.Value), ct)).ToList();
        var lastMoltByBox = molts
            .Where(m => m.BoxId.HasValue)
            .GroupBy(m => m.BoxId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MoltTime).First());

        // Layout indices: group by area then row
        var layoutIndex = BuildLayoutIndices(boxList, rowMap);

        var items = new List<BoxOverviewItemDto>();
        foreach (var box in boxList)
        {
            rowMap.TryGetValue(box.FarmingRowId, out var row);
            var areaId = row?.FarmingAreaId ?? Guid.Empty;
            areas.TryGetValue(areaId, out var area);

            crabsByBox.TryGetValue(box.Id, out var boxCrabs);
            boxCrabs ??= [];
            var alive = boxCrabs.Where(c => c.Status == CrabStatus.Alive).ToList();
            var primary = alive.OrderByDescending(c => c.CreatedAt).FirstOrDefault()
                          ?? boxCrabs.OrderByDescending(c => c.CreatedAt).FirstOrDefault();

            string? batch = null;
            string? crabType = null;
            if (primary?.CrabLotId is Guid lotId && lots.TryGetValue(lotId, out var lot))
            {
                batch = lot.LotCode;
                crabType = string.IsNullOrWhiteSpace(lot.SupplierName) ? "Cua lột" : lot.SupplierName;
            }

            openAllocs.TryGetValue(box.Id, out var alloc);
            lastMoltByBox.TryGetValue(box.Id, out var lastMolt);

            var health = ComputeBoxHealth(box, waterScore, deviceScore, alerts.Count, lastMolt);
            var healthStatus = MapHealthStatus(box, health.Score, deviceSnap.IsOnline);
            var ai = BuildAiTip(box, alerts, healthStatus);
            var priority = healthStatus switch
            {
                "critical" => "high",
                "warning" => "medium",
                "offline" => "medium",
                _ => ai?.HasRecommendation == true ? "medium" : "low"
            };

            layoutIndex.TryGetValue(box.Id, out var layout);
            layout ??= new BoxMapLayoutDto(0.5, 0.5, 0, 0);

            var alertCount = CountRelevantAlerts(box, alerts);
            var latestAlert = alerts
                .Where(a => IsAlertRelevant(box, a))
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            var lastUpdated = new[]
                {
                    box.UpdatedAt,
                    box.CreatedAt,
                    water.MeasuredAt,
                    devices.MaxBy(d => d.LastSeenAt)?.LastSeenAt,
                    lastMolt?.MoltTime,
                    alloc?.StartTime
                }
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .DefaultIfEmpty(DateTime.UtcNow)
                .Max();

            DateTime? harvestAt = null;
            if (string.Equals(box.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
                harvestAt = DateTime.UtcNow.AddHours(6);
            else if (alloc?.StartTime is DateTime started)
                harvestAt = started.AddDays(21);

            var waterTestDue = water.Alarms.Count > 0
                || (water.MeasuredAt is DateTime wm && wm < DateTime.UtcNow.AddHours(-12));
            var videoDue = string.Equals(box.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)
                || (lastMolt is null && box.IsOccupied);

            items.Add(new BoxOverviewItemDto(
                Id: box.Id,
                Code: box.Code,
                Name: box.Code,
                QrCode: box.Code,
                FarmingAreaId: areaId,
                FarmName: area?.Name ?? farmName,
                FarmingRowId: box.FarmingRowId,
                RowName: row?.Name,
                AreaName: area?.Name,
                Status: box.Status,
                IsOccupied: box.IsOccupied,
                HealthStatus: healthStatus,
                Health: health,
                CrabCount: alive.Count > 0 ? alive.Count : (box.IsOccupied ? 1 : 0),
                CrabType: crabType,
                Batch: batch,
                MoltingStage: primary?.MoltingStage,
                Water: water,
                Devices: deviceSnap,
                AlertCount: alertCount,
                LatestAlertTitle: latestAlert?.Message,
                LatestAlertAt: latestAlert?.CreatedAt,
                AiRecommendation: ai,
                Layout: layout,
                LastUpdated: lastUpdated,
                ExpectedHarvestAt: harvestAt,
                WaterTestDue: waterTestDue,
                VideoDue: videoDue,
                Priority: priority,
                CrabCondition: WorstCrabCondition(boxCrabs)));
        }

        var summary = new BoxesFarmSummaryDto(
            Total: items.Count,
            Healthy: items.Count(i => i.HealthStatus == "healthy"),
            Warning: items.Count(i => i.HealthStatus == "warning"),
            Critical: items.Count(i => i.HealthStatus == "critical"),
            Offline: items.Count(i => i.HealthStatus == "offline"),
            WithAiRecommendation: items.Count(i => i.AiRecommendation?.HasRecommendation == true));

        return ApiResponse<BoxesOverviewResponseDto>.Ok(new BoxesOverviewResponseDto(
            FarmingAreaId: scopeAreaId,
            FarmName: farmName,
            Summary: summary,
            AreaWater: water,
            AreaDevices: deviceSnap,
            Items: items,
            SyncedAt: DateTime.UtcNow));
    }

    private async Task<(
        List<WaterSystem> WaterSystems,
        List<Sensor> Sensors,
        List<Device> Devices,
        List<WaterMeasurement> Measurements,
        List<Alert> Alerts)> LoadAreaIoTAsync(Guid? farmingAreaId, CancellationToken ct)
    {
        var allSensors = (await _uow.Sensors.GetAllAsync(ct)).ToList();
        var allDevices = (await _uow.Devices.GetAllAsync(ct)).ToList();
        var allMeas = (await _uow.WaterMeasurements.GetAllAsync(ct)).ToList();
        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();

        if (farmingAreaId is null || farmingAreaId == Guid.Empty)
            return ([], allSensors, allDevices, allMeas, activeAlerts);

        var ws = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == farmingAreaId.Value, ct)).ToList();
        var wsIds = ws.Select(w => w.Id).ToHashSet();
        var sensors = allSensors
            .Where(s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
            .ToList();
        var sensorIds = sensors.Select(s => s.Id).ToHashSet();
        var deviceIds = sensors.Where(s => s.DeviceId.HasValue).Select(s => s.DeviceId!.Value).ToHashSet();
        var devices = allDevices.Where(d => deviceIds.Contains(d.Id)).ToList();
        if (devices.Count == 0)
            devices = allDevices; // fallback if sensors not linked yet
        var meas = allMeas.Where(m => sensorIds.Contains(m.SensorId)).ToList();
        var alerts = activeAlerts
            .Where(a => a.SensorId is Guid sid && sensorIds.Contains(sid))
            .ToList();

        return (ws, sensors, devices, meas, alerts);
    }

    private static BoxWaterSnapshotDto BuildWaterSnapshot(
        List<Sensor> sensors, List<WaterMeasurement> measurements)
    {
        var latestBySensor = measurements
            .GroupBy(m => m.SensorId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasuredAt).First());

        decimal? temp = null, ph = null, dout = null;
        DateTime? measuredAt = null;
        var alarms = new List<string>();

        foreach (var s in sensors.Where(s => s.IsActive))
        {
            if (!latestBySensor.TryGetValue(s.Id, out var latest)) continue;
            measuredAt = MaxDate(measuredAt, latest.MeasuredAt);
            var type = s.SensorType?.Trim().ToLowerInvariant() ?? "";
            if (type is "temperature" or "temp" or "nhiệt độ")
                temp = latest.Value;
            else if (type is "ph")
                ph = latest.Value;
            else if (type is "do" or "dissolvedoxygen" or "dissolved_oxygen" or "oxy")
                dout = latest.Value;

            if (s.MinThreshold.HasValue && latest.Value < s.MinThreshold.Value)
                alarms.Add($"{s.SensorType} below_min");
            else if (s.MaxThreshold.HasValue && latest.Value > s.MaxThreshold.Value)
                alarms.Add($"{s.SensorType} above_max");
        }

        return new BoxWaterSnapshotDto(temp, ph, dout, measuredAt, alarms);
    }

    private static BoxDeviceSnapshotDto BuildDeviceSnapshot(List<Device> devices)
    {
        if (devices.Count == 0)
            return new BoxDeviceSnapshotDto(true, 0, 0);
        var online = devices.Count(d => d.Status == DeviceStatus.Online);
        return new BoxDeviceSnapshotDto(online > 0, online, devices.Count);
    }

    private static (int WaterScore, int DeviceScore) ComputeAreaScores(
        List<Sensor> sensors, List<Device> devices, List<Alert> alerts)
    {
        var deviceScore = devices.Count == 0
            ? 80
            : (int)Math.Round(devices.Count(d => d.Status == DeviceStatus.Online) * 100.0 / devices.Count);

        var activeSensors = sensors.Count(s => s.IsActive);
        var staleCutoff = DateTime.UtcNow.AddHours(-2);
        var fresh = sensors.Count(s => s.IsActive && s.LastSeenAt != null && s.LastSeenAt > staleCutoff);
        var waterScore = activeSensors == 0
            ? 82
            : (int)Math.Round(Math.Max(fresh, activeSensors * 0.7) * 100.0 / activeSensors);
        waterScore = Math.Clamp(waterScore - alerts.Count(a => a.Severity >= AlertSeverity.Warning) * 4, 0, 100);

        return (waterScore, deviceScore);
    }

    private static BoxHealthScoreDto ComputeBoxHealth(
        Box box, int waterScore, int deviceScore, int alertCount, MoltingRecord? lastMolt)
    {
        var status = box.Status ?? "";
        var crabScore = 85;
        if (string.Equals(status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase))
            crabScore = 35;
        else if (string.Equals(status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            crabScore = 55;
        else if (string.Equals(status, BoxStatuses.Empty, StringComparison.OrdinalIgnoreCase) && !box.IsOccupied)
            crabScore = 90;
        else if (string.Equals(status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
            crabScore = 78;
        else if (string.Equals(status, BoxStatuses.Harvested, StringComparison.OrdinalIgnoreCase))
            crabScore = 88;
        else if (string.Equals(status, BoxStatuses.Active, StringComparison.OrdinalIgnoreCase) || box.IsOccupied)
            crabScore = 92;

        crabScore = Math.Clamp(crabScore - Math.Min(alertCount, 5) * 3, 0, 100);

        var score = (int)Math.Round(waterScore * 0.35 + crabScore * 0.40 + deviceScore * 0.25);
        score = Math.Clamp(score, 0, 100);

        var (level, label) = score switch
        {
            >= 85 => ("excellent", "Excellent"),
            >= 70 => ("good", "Good"),
            >= 50 => ("warning", "Warning"),
            _ => ("danger", "Critical")
        };

        var trend = "stable";
        if (lastMolt is not null &&
            string.Equals(lastMolt.Result, "success", StringComparison.OrdinalIgnoreCase) &&
            lastMolt.MoltTime > DateTime.UtcNow.AddDays(-3))
            trend = "improving";
        else if (string.Equals(status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase) || alertCount >= 2)
            trend = "declining";

        var confidence = Math.Clamp(70 + deviceScore / 5 + (alertCount == 0 ? 10 : 0), 60, 99);

        var explanation =
            $"Điểm tổng hợp: nước {waterScore}%, cua {crabScore}%, thiết bị {deviceScore}% (trọng số 35/40/25).";

        return new BoxHealthScoreDto(score, confidence, trend, label, level, explanation);
    }

    private static string MapHealthStatus(Box box, int score, bool areaOnline)
    {
        var status = box.Status ?? "";
        if (!areaOnline && string.Equals(status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            return "offline";
        if (string.Equals(status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase))
            return "offline";
        if (string.Equals(status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase) || score < 50)
            return "critical";
        if (score < 70 || string.Equals(status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
            return "warning";
        return "healthy";
    }

    /// <summary>
    /// Tình trạng đáng chú ý nhất trong số cua đang ở hộp — để màn Boxes tô màu
    /// theo đúng thứ nông dân đánh dấu hằng ngày, không chỉ theo điểm sức khỏe.
    /// Trả về key API (khớp CrabConditions.ToApi) hoặc null nếu hộp chưa có cua.
    /// </summary>
    private static string? WorstCrabCondition(IReadOnlyList<Crab> crabs)
    {
        CrabCondition? worst = null;
        var worstRank = -1;
        foreach (var crab in crabs)
        {
            var rank = crab.Condition switch
            {
                CrabCondition.Dead => 6,
                CrabCondition.Problem => 5,
                CrabCondition.Weak => 4,
                CrabCondition.Molting => 3,
                CrabCondition.Softshell => 2,
                CrabCondition.Premolt => 1,
                CrabCondition.Harvested or CrabCondition.Sold => -1,
                _ => 0
            };
            if (rank > worstRank)
            {
                worstRank = rank;
                worst = crab.Condition;
            }
        }
        return worst is null ? null : CrabConditions.ToApi(worst.Value);
    }

    private static BoxAiTipDto? BuildAiTip(Box box, List<Alert> alerts, string healthStatus)
    {
        if (string.Equals(box.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
        {
            return new BoxAiTipDto(
                box.Id.ToString(),
                $"Thu hoạch box {box.Code}",
                $"Box {box.Code} đang lột — kiểm tra softshell và thu hoạch khi đạt.",
                "high",
                88,
                true);
        }

        if (healthStatus is "critical" or "warning")
        {
            var alert = alerts.OrderByDescending(a => a.Severity).ThenByDescending(a => a.CreatedAt).FirstOrDefault();
            return new BoxAiTipDto(
                alert?.Id.ToString() ?? box.Id.ToString(),
                healthStatus == "critical" ? "Ưu tiên kiểm tra nước" : "Đề xuất quay video AI",
                string.IsNullOrWhiteSpace(alert?.Message)
                    ? $"Box {box.Code} cần theo dõi thêm."
                    : alert!.Message,
                healthStatus == "critical" ? "high" : "medium",
                healthStatus == "critical" ? 92 : 80,
                true);
        }

        return new BoxAiTipDto("none", "", "", "low", 99, false);
    }

    private static Dictionary<Guid, BoxMapLayoutDto> BuildLayoutIndices(
        List<Box> boxes, Dictionary<Guid, FarmingRow> rowMap)
    {
        var areaRowGroups = boxes
            .GroupBy(b => rowMap.TryGetValue(b.FarmingRowId, out var r) ? r.FarmingAreaId : Guid.Empty)
            .OrderBy(g => g.Key)
            .ToList();

        var result = new Dictionary<Guid, BoxMapLayoutDto>();
        var areaIdx = 0;
        foreach (var areaGroup in areaRowGroups)
        {
            var rowsInArea = areaGroup
                .GroupBy(b => b.FarmingRowId)
                .OrderBy(g => rowMap.TryGetValue(g.Key, out var r) ? r.Name : g.Key.ToString())
                .ToList();

            var rowIdx = 0;
            foreach (var rowGroup in rowsInArea)
            {
                var ordered = rowGroup.OrderBy(b => b.Code).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var gridX = areaIdx * 8.0 + (i % 6) + 0.5;
                    var gridY = rowIdx + 0.5 + (areaIdx % 2) * 0.1;
                    result[ordered[i].Id] = new BoxMapLayoutDto(gridX, gridY, rowIdx, i);
                }
                rowIdx++;
            }
            areaIdx++;
        }
        return result;
    }

    private static int CountRelevantAlerts(Box box, List<Alert> alerts)
    {
        // Alerts are sensor-scoped; surface area alerts on occupied / warning boxes.
        if (alerts.Count == 0) return 0;
        var mentioning = alerts.Count(a =>
            !string.IsNullOrWhiteSpace(a.Message) &&
            a.Message.Contains(box.Code, StringComparison.OrdinalIgnoreCase));
        if (mentioning > 0) return mentioning;
        if (string.Equals(box.Status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase))
            return Math.Min(alerts.Count, 3);
        if (string.Equals(box.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
            return Math.Min(alerts.Count(a => a.Severity >= AlertSeverity.Warning), 2);
        return alerts.Count(a => a.Severity == AlertSeverity.Critical) > 0 && box.IsOccupied ? 1 : 0;
    }

    private static bool IsAlertRelevant(Box box, Alert alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.Message) &&
            alert.Message.Contains(box.Code, StringComparison.OrdinalIgnoreCase))
            return true;
        return box.IsOccupied && alert.Severity >= AlertSeverity.Warning;
    }

    private static DateTime? MaxDate(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }
}
