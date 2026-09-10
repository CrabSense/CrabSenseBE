using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Alert;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Cảnh báo thông minh:
/// 1) CRUD ngưỡng theo SensorType (nhiệt độ, pH, mặn, DO, mực nước…).
/// 2) EvaluateMeasurement: vượt min/max → tạo Alert + Notify.
/// 3) CheckDisconnects: sensor/device không thấy quá N phút → Alert.
/// 4) Enriched AlertDto cho Mobile Alerts Command Center.
/// </summary>
public class AlertService : IAlertService
{
    private readonly IUnitOfWork _uow;
    private readonly INotificationService _notifications;

    public AlertService(IUnitOfWork uow, INotificationService notifications)
    {
        _uow = uow;
        _notifications = notifications;
    }

    // ─── Thresholds ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<AlertThresholdDto>>> GetThresholdsAsync(CancellationToken ct = default)
    {
        var items = await _uow.AlertThresholds.GetAllAsync(ct);
        return ApiResponse<IEnumerable<AlertThresholdDto>>.Ok(items.Select(MapThreshold));
    }

    public async Task<ApiResponse<AlertThresholdDto>> CreateThresholdAsync(
        CreateAlertThresholdRequest req, CancellationToken ct = default)
    {
        if (req.MinValue >= req.MaxValue)
            throw AppException.BadRequest("MinValue must be less than MaxValue.");

        var severity = ParseSeverity(req.Severity);
        var entity = new AlertThreshold
        {
            SensorType = req.SensorType,
            MinValue = req.MinValue,
            MaxValue = req.MaxValue,
            Severity = severity,
            IsActive = true
        };
        await _uow.AlertThresholds.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<AlertThresholdDto>.Ok(MapThreshold(entity), "Created.");
    }

    public async Task<ApiResponse<AlertThresholdDto>> UpdateThresholdAsync(
        Guid id, UpdateAlertThresholdRequest req, CancellationToken ct = default)
    {
        var entity = await _uow.AlertThresholds.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("AlertThreshold");
        entity.MinValue = req.MinValue;
        entity.MaxValue = req.MaxValue;
        entity.Severity = ParseSeverity(req.Severity);
        entity.IsActive = req.IsActive;
        _uow.AlertThresholds.Update(entity);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<AlertThresholdDto>.Ok(MapThreshold(entity));
    }

    public async Task<ApiResponse> DeleteThresholdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _uow.AlertThresholds.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("AlertThreshold");
        _uow.AlertThresholds.Remove(entity);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Alert threshold deleted.");
    }

    // ─── Alerts ─────────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<AlertDto>>> GetAlertsAsync(
        bool? activeOnly = true,
        Guid? farmingAreaId = null,
        Guid? boxId = null,
        CancellationToken ct = default)
    {
        farmingAreaId = await ResolveFarmingAreaFromBoxAsync(boxId, farmingAreaId, ct);

        IEnumerable<Alert> items;
        if (activeOnly == true)
            items = await _uow.Alerts.FindAsync(
                a => a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged, ct);
        else
            items = await _uow.Alerts.GetAllAsync(ct);

        items = await FilterByFarmingAreaAsync(items, farmingAreaId, ct);
        var dtos = await MapAlertsAsync(
            items.OrderByDescending(a => a.CreatedAt), ct);
        return ApiResponse<IEnumerable<AlertDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<IEnumerable<AlertDto>>> GetHistoryAsync(
        int days = 30,
        Guid? farmingAreaId = null,
        CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var items = await _uow.Alerts.FindAsync(
            a => (a.Status == AlertStatus.Resolved || a.Status == AlertStatus.Acknowledged)
                 && a.CreatedAt >= cutoff,
            ct);
        items = await FilterByFarmingAreaAsync(items, farmingAreaId, ct);
        var dtos = await MapAlertsAsync(
            items.OrderByDescending(a => a.CreatedAt), ct);
        return ApiResponse<IEnumerable<AlertDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<AlertDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        var dto = (await MapAlertsAsync(new[] { alert }, ct)).First();
        return ApiResponse<AlertDto>.Ok(dto);
    }

    public async Task<ApiResponse<AlertUnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var count = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).Count();
        return ApiResponse<AlertUnreadCountDto>.Ok(new AlertUnreadCountDto(count, count));
    }

    public async Task<ApiResponse<AlertDto>> AcknowledgeAsync(
        Guid id, AcknowledgeAlertRequest req, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = req.UserId;
        _uow.Alerts.Update(alert);
        await _uow.SaveChangesAsync(ct);
        var dto = (await MapAlertsAsync(new[] { alert }, ct)).First();
        return ApiResponse<AlertDto>.Ok(dto);
    }

    public async Task<ApiResponse<AlertDto>> ResolveAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        alert.Status = AlertStatus.Resolved;
        _uow.Alerts.Update(alert);
        await _uow.SaveChangesAsync(ct);
        var dto = (await MapAlertsAsync(new[] { alert }, ct)).First();
        return ApiResponse<AlertDto>.Ok(dto);
    }

    // ─── Engine ─────────────────────────────────────────────────────────────

    public async Task EvaluateMeasurementAsync(Sensor sensor, decimal value, CancellationToken ct = default)
    {
        decimal? min = sensor.MinThreshold;
        decimal? max = sensor.MaxThreshold;
        AlertSeverity severity = AlertSeverity.Warning;
        Guid? thresholdId = null;

        if (min is null || max is null)
        {
            var thr = (await _uow.AlertThresholds.FindAsync(
                t => t.IsActive && t.SensorType == sensor.SensorType, ct)).FirstOrDefault();
            if (thr is null) return;
            min = thr.MinValue;
            max = thr.MaxValue;
            severity = thr.Severity;
            thresholdId = thr.Id;
        }

        if (value >= min && value <= max) return;

        var exists = await _uow.Alerts.AnyAsync(
            a => a.SensorId == sensor.Id && a.Status == AlertStatus.Active, ct);
        if (exists) return;

        var msg = value < min
            ? $"{sensor.SensorType} thấp: {value} < {min} (sensor {sensor.SensorCode})"
            : $"{sensor.SensorType} cao: {value} > {max} (sensor {sensor.SensorCode})";

        var alert = new Alert
        {
            SensorId = sensor.Id,
            AlertThresholdId = thresholdId,
            Message = msg,
            Severity = severity,
            Status = AlertStatus.Active,
            TriggerValue = value
        };
        await _uow.Alerts.AddAsync(alert, ct);
        await _uow.SaveChangesAsync(ct);

        await NotifyOperatorsAsync(alert.Message, alert.Id, ct);
    }

    public async Task<ApiResponse<int>> CheckDisconnectsAsync(
        int timeoutMinutes = 15, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);
        var created = 0;

        var sensors = await _uow.Sensors.FindAsync(
            s => s.IsActive && s.LastSeenAt != null && s.LastSeenAt < cutoff, ct);
        foreach (var sensor in sensors)
        {
            var exists = await _uow.Alerts.AnyAsync(
                a => a.SensorId == sensor.Id && a.Status == AlertStatus.Active
                     && a.Message.Contains("mất kết nối"), ct);
            if (exists) continue;

            var alert = new Alert
            {
                SensorId = sensor.Id,
                Message = $"Cảm biến mất kết nối: {sensor.SensorCode} (>{timeoutMinutes} phút)",
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.Active
            };
            await _uow.Alerts.AddAsync(alert, ct);
            created++;
            await NotifyOperatorsAsync(alert.Message, alert.Id, ct);
        }

        var devices = await _uow.Devices.FindAsync(
            d => d.LastSeenAt != null && d.LastSeenAt < cutoff
                 && d.Status != DeviceStatus.Maintenance, ct);
        foreach (var device in devices)
        {
            device.Status = DeviceStatus.Offline;
            _uow.Devices.Update(device);

            var kind = string.IsNullOrWhiteSpace(device.DeviceType) ? "thiết bị" : device.DeviceType;
            var msg = $"{kind} mất kết nối: {device.DeviceCode}";
            var exists = await _uow.Alerts.AnyAsync(
                a => a.Status == AlertStatus.Active && a.Message == msg, ct);
            if (exists) continue;

            var alert = new Alert
            {
                Message = msg,
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.Active
            };
            await _uow.Alerts.AddAsync(alert, ct);
            created++;
            await NotifyOperatorsAsync(alert.Message, alert.Id, ct);
        }

        if (created > 0)
            await _uow.SaveChangesAsync(ct);

        return ApiResponse<int>.Ok(created, $"Created {created} disconnect alert(s).");
    }

    // ─── Mapping / enrichment ───────────────────────────────────────────────

    private async Task<Guid?> ResolveFarmingAreaFromBoxAsync(
        Guid? boxId, Guid? farmingAreaId, CancellationToken ct)
    {
        if (boxId is Guid bid && bid != Guid.Empty)
        {
            var box = await _uow.Boxes.GetByIdAsync(bid, ct)
                ?? throw AppException.NotFound("Box");
            var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
            if (row is not null)
                return row.FarmingAreaId;
        }
        return farmingAreaId;
    }

    private async Task<IEnumerable<Alert>> FilterByFarmingAreaAsync(
        IEnumerable<Alert> items, Guid? farmingAreaId, CancellationToken ct)
    {
        if (farmingAreaId is not Guid areaId || areaId == Guid.Empty)
            return items;

        var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
            .Select(w => w.Id)
            .ToHashSet();
        var sensorIds = (await _uow.Sensors.FindAsync(
                s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value), ct))
            .Select(s => s.Id)
            .ToHashSet();

        // Keep sensor-scoped alerts for the area; also keep system/device alerts (no sensor).
        return items.Where(a =>
            a.SensorId is null ||
            (a.SensorId is Guid sid && sensorIds.Contains(sid)));
    }

    private async Task<List<AlertDto>> MapAlertsAsync(IEnumerable<Alert> alerts, CancellationToken ct)
    {
        var list = alerts.ToList();
        if (list.Count == 0) return new List<AlertDto>();

        var sensorIds = list.Where(a => a.SensorId.HasValue).Select(a => a.SensorId!.Value).Distinct().ToList();
        var thresholdIds = list.Where(a => a.AlertThresholdId.HasValue)
            .Select(a => a.AlertThresholdId!.Value).Distinct().ToList();

        var sensors = sensorIds.Count == 0
            ? new List<Sensor>()
            : (await _uow.Sensors.FindAsync(s => sensorIds.Contains(s.Id), ct)).ToList();
        var sensorMap = sensors.ToDictionary(s => s.Id);

        var wsIds = sensors.Where(s => s.WaterSystemId.HasValue)
            .Select(s => s.WaterSystemId!.Value).Distinct().ToList();
        var waterSystems = wsIds.Count == 0
            ? new List<WaterSystem>()
            : (await _uow.WaterSystems.FindAsync(w => wsIds.Contains(w.Id), ct)).ToList();
        var wsMap = waterSystems.ToDictionary(w => w.Id);

        var areaIds = waterSystems.Where(w => w.FarmingAreaId.HasValue)
            .Select(w => w.FarmingAreaId!.Value).Distinct().ToList();
        var areas = areaIds.Count == 0
            ? new List<FarmingArea>()
            : (await _uow.FarmingAreas.FindAsync(a => areaIds.Contains(a.Id), ct)).ToList();
        var areaMap = areas.ToDictionary(a => a.Id);

        var thresholds = thresholdIds.Count == 0
            ? new List<AlertThreshold>()
            : (await _uow.AlertThresholds.FindAsync(t => thresholdIds.Contains(t.Id), ct)).ToList();
        var thrMap = thresholds.ToDictionary(t => t.Id);

        // Fallback thresholds by sensor type
        var typeThresholds = (await _uow.AlertThresholds.FindAsync(t => t.IsActive, ct))
            .GroupBy(t => t.SensorType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<AlertDto>(list.Count);
        foreach (var a in list)
        {
            Sensor? sensor = null;
            if (a.SensorId is Guid sid)
                sensorMap.TryGetValue(sid, out sensor);

            WaterSystem? ws = null;
            if (sensor?.WaterSystemId is Guid wid)
                wsMap.TryGetValue(wid, out ws);

            FarmingArea? area = null;
            if (ws?.FarmingAreaId is Guid aid)
                areaMap.TryGetValue(aid, out area);

            AlertThreshold? thr = null;
            if (a.AlertThresholdId is Guid tid)
                thrMap.TryGetValue(tid, out thr);
            if (thr is null && sensor is not null)
                typeThresholds.TryGetValue(sensor.SensorType, out thr);

            var min = sensor?.MinThreshold ?? thr?.MinValue;
            var max = sensor?.MaxThreshold ?? thr?.MaxValue;
            var sensorType = sensor?.SensorType ?? InferSensorType(a.Message);
            var category = InferCategory(a.Message, sensorType);
            var title = BuildTitle(a.Message, sensorType);
            var (score, explanation, sla) = ComputePriority(a, min, max);
            var (aiTip, confidence) = BuildAiTip(category, sensorType, a, min, max);
            var location = area?.Name
                ?? (sensor is null ? "Hệ thống" : sensor.SensorCode);

            result.Add(new AlertDto(
                a.Id,
                a.SensorId,
                a.Message,
                a.Severity.ToString(),
                a.Status.ToString(),
                a.TriggerValue,
                a.CreatedAt,
                a.AcknowledgedAt,
                title,
                category,
                sensor?.SensorCode,
                sensorType,
                sensor?.Unit,
                area?.Id,
                area?.Name,
                location,
                min,
                max,
                score,
                explanation,
                sla,
                aiTip,
                confidence,
                a.AcknowledgedBy));
        }

        return result;
    }

    private static string InferSensorType(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("do") || m.Contains("oxy") || m.Contains("dissolved")) return "DO";
        if (m.Contains("ph")) return "pH";
        if (m.Contains("nhiệt") || m.Contains("temp")) return "Temperature";
        if (m.Contains("mặn") || m.Contains("salinity")) return "Salinity";
        if (m.Contains("nh3") || m.Contains("amoni")) return "NH3";
        if (m.Contains("no2")) return "NO2";
        if (m.Contains("no3")) return "NO3";
        if (m.Contains("mực nước") || m.Contains("level")) return "WaterLevel";
        if (m.Contains("camera")) return "Camera";
        if (m.Contains("esp32") || m.Contains("gateway")) return "Device";
        if (m.Contains("cảm biến") || m.Contains("sensor") || m.Contains("mất kết nối")) return "Sensor";
        return "System";
    }

    private static string InferCategory(string message, string? sensorType)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("cua") || m.Contains("crab") || m.Contains("lột") || m.Contains("ai phát hiện"))
            return "CrabHealth";
        if (m.Contains("camera") || m.Contains("confidence") || m.Contains("video") || m.Contains("ánh sáng"))
            return "AI";
        if (m.Contains("lịch") || m.Contains("cho ăn") || m.Contains("thu hoạch") || m.Contains("công việc"))
            return "Operations";
        if (m.Contains("sync") || m.Contains("api") || m.Contains("storage") || m.Contains("permission"))
            return "System";
        if (m.Contains("mất kết nối") || m.Contains("offline") || m.Contains("esp32")
            || m.Contains("pump") || m.Contains("valve") || m.Contains("gateway")
            || string.Equals(sensorType, "Device", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sensorType, "Camera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sensorType, "Sensor", StringComparison.OrdinalIgnoreCase))
            return "Device";

        return "WaterQuality";
    }

    private static string BuildTitle(string message, string? sensorType)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Cảnh báo hệ thống";
        var cut = message.Split('(')[0].Trim();
        if (cut.Length > 72) cut = cut[..69] + "...";
        return cut;
    }

    private static (int Score, string Explanation, string Sla) ComputePriority(
        Alert alert, decimal? min, decimal? max)
    {
        var score = alert.Severity switch
        {
            AlertSeverity.Critical => 88,
            AlertSeverity.Warning => 68,
            _ => 42
        };

        var ageMinutes = (DateTime.UtcNow - alert.CreatedAt).TotalMinutes;
        if (ageMinutes > 60) score += 4;
        if (ageMinutes > 180) score += 4;

        if (alert.TriggerValue is decimal tv && min is decimal lo && max is decimal hi && hi > lo)
        {
            var span = (double)(hi - lo);
            double breach;
            if (tv < lo) breach = (double)(lo - tv) / span;
            else if (tv > hi) breach = (double)(tv - hi) / span;
            else breach = 0;
            score += (int)Math.Clamp(breach * 20, 0, 12);
        }

        if (alert.Status == AlertStatus.Acknowledged) score = Math.Max(40, score - 8);
        if (alert.Status == AlertStatus.Resolved) score = Math.Min(score, 25);

        score = Math.Clamp(score, 0, 100);

        var band = score >= 85 ? "Critical" : score >= 70 ? "High" : score >= 50 ? "Medium" : "Low";
        var sla = score >= 85 ? "Cần xử lý trong 10 phút"
            : score >= 70 ? "Cần xử lý trong 1 giờ"
            : score >= 50 ? "Theo dõi trong ca"
            : "Theo dõi định kỳ";

        var explanation =
            $"Severity {alert.Severity}, tồn tại {(int)ageMinutes} phút → Priority {score} ({band}).";
        return (score, explanation, sla);
    }

    private static (string? Tip, int? Confidence) BuildAiTip(
        string category, string? sensorType, Alert alert, decimal? min, decimal? max)
    {
        if (alert.Status == AlertStatus.Resolved)
            return (null, null);

        var type = (sensorType ?? "").ToLowerInvariant();
        if (category == "Device")
            return ("Thử kết nối lại thiết bị và kiểm tra nguồn / gateway.", 74);

        if (type.Contains("do") || alert.Message.Contains("DO", StringComparison.OrdinalIgnoreCase))
            return ("Bật sục khí và đo lại DO sau 10–15 phút.", 90);

        if (type.Contains("ph"))
            return ("Điều chỉnh nước / thay một phần nước, đo lại pH sau 30 phút.", 84);

        if (type.Contains("temp") || type.Contains("nhiệt"))
            return ("Kiểm tra hệ thống làm mát / che nắng khu nuôi.", 82);

        if (type.Contains("nh3") || type.Contains("amoni"))
            return ("Thay 15–20% nước và giảm cho ăn tạm thời, đo lại NH3 sau 30 phút.", 88);

        if (category == "WaterQuality")
        {
            var range = min is not null && max is not null ? $" (ngưỡng {min}–{max})" : "";
            return ($"Kiểm tra cảm biến và thông số nước{range}, ghi nhận kết quả xử lý.", 70);
        }

        return ("Kiểm tra nguồn gốc cảnh báo và cập nhật trạng thái sau khi xử lý.", 65);
    }

    private async Task NotifyOperatorsAsync(string message, Guid alertId, CancellationToken ct)
    {
        var users = await _uow.Users.FindAsync(
            u => u.IsActive && (u.Role == UserRole.FarmOwner || u.Role == UserRole.Staff), ct);
        var ids = users.Select(u => u.Id).ToList();
        if (ids.Count == 0) return;
        await _notifications.NotifyUsersAsync(ids, "CrabSense Alert", message, alertId, ct);
    }

    private static AlertSeverity ParseSeverity(string? s) =>
        Enum.TryParse<AlertSeverity>(s, true, out var v) ? v : AlertSeverity.Warning;

    private static AlertThresholdDto MapThreshold(AlertThreshold t) =>
        new(t.Id, t.SensorType, t.MinValue, t.MaxValue, t.Severity.ToString(), t.IsActive);
}
