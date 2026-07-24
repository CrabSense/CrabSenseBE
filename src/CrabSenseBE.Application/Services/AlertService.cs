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
        // Resolve farming area from box when boxId is provided.
        if (boxId is Guid bid && bid != Guid.Empty)
        {
            var box = await _uow.Boxes.GetByIdAsync(bid, ct)
                ?? throw AppException.NotFound("Box");
            var row = await _uow.FarmingRows.GetByIdAsync(box.FarmingRowId, ct);
            if (row is not null)
                farmingAreaId = row.FarmingAreaId;
        }

        IEnumerable<Alert> items;
        if (activeOnly == true)
            items = await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct);
        else
            items = await _uow.Alerts.GetAllAsync(ct);

        if (farmingAreaId is Guid areaId && areaId != Guid.Empty)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var sensorIds = (await _uow.Sensors.FindAsync(
                    s => s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value), ct))
                .Select(s => s.Id)
                .ToHashSet();
            items = items.Where(a => a.SensorId is Guid sid && sensorIds.Contains(sid));
        }

        return ApiResponse<IEnumerable<AlertDto>>.Ok(
            items.OrderByDescending(a => a.CreatedAt).Select(MapAlert));
    }

    public async Task<ApiResponse<AlertDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        return ApiResponse<AlertDto>.Ok(MapAlert(alert));
    }

    public async Task<ApiResponse<object>> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var count = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).Count();
        return ApiResponse<object>.Ok(new { count, unreadCount = count });
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
        return ApiResponse<AlertDto>.Ok(MapAlert(alert));
    }

    public async Task<ApiResponse<AlertDto>> ResolveAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        alert.Status = AlertStatus.Resolved;
        _uow.Alerts.Update(alert);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<AlertDto>.Ok(MapAlert(alert));
    }

    // ─── Engine ─────────────────────────────────────────────────────────────

    public async Task EvaluateMeasurementAsync(Sensor sensor, decimal value, CancellationToken ct = default)
    {
        // Ưu tiên ngưỡng trên sensor; fallback AlertThreshold theo SensorType
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

        // Tránh spam: nếu đã có Active cùng sensor chưa acknowledge thì bỏ qua
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

        // Cảm biến không gửi data quá timeout
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

        // Thiết bị / camera gateway offline
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

    private async Task NotifyOperatorsAsync(string message, Guid alertId, CancellationToken ct)
    {
        // Gửi tới chủ trại + nhân viên đang active
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

    private static AlertDto MapAlert(Alert a) =>
        new(a.Id, a.SensorId, a.Message, a.Severity.ToString(), a.Status.ToString(),
            a.TriggerValue, a.CreatedAt, a.AcknowledgedAt);
}
