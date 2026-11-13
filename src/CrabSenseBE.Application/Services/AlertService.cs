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
    private readonly FarmAreaScope _areas;

    public AlertService(IUnitOfWork uow, INotificationService notifications, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _notifications = notifications;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    // ─── Thresholds ─────────────────────────────────────────────────────────

    public async Task<ApiResponse<IEnumerable<AlertThresholdDto>>> GetThresholdsAsync(CancellationToken ct = default)
    {
        var items = (await _uow.AlertThresholds.GetAllAsync(ct) ?? Enumerable.Empty<AlertThreshold>()).AsEnumerable();
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is not null)
            items = items.Where(item => item.FarmingAreaId is Guid areaId && scope.Contains(areaId));
        return ApiResponse<IEnumerable<AlertThresholdDto>>.Ok(items.Select(MapThreshold));
    }

    public async Task<ApiResponse<AlertThresholdDto>> CreateThresholdAsync(
        CreateAlertThresholdRequest req, CancellationToken ct = default)
    {
        ValidateThresholdRange(req.MinValue, req.MaxValue);
        var areaId = await RequireThresholdAreaAsync(req.FarmingAreaId, ct);
        var waterSystemId = await RequireWaterSystemAsync(req.WaterSystemId, areaId, ct);

        var severity = ParseSeverity(req.Severity);
        var entity = new AlertThreshold
        {
            SensorType = req.SensorType,
            MinValue = req.MinValue,
            MaxValue = req.MaxValue,
            Severity = severity,
            IsActive = true,
            FarmingAreaId = areaId,
            WaterSystemId = waterSystemId
        };
        await _uow.AlertThresholds.AddAsync(entity, ct);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "CreateAlertThreshold", "AlertThreshold", entity.Id, entity.SensorType, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<AlertThresholdDto>.Ok(MapThreshold(entity), "Created.");
    }

    public async Task<ApiResponse<AlertThresholdDto>> UpdateThresholdAsync(
        Guid id, UpdateAlertThresholdRequest req, CancellationToken ct = default)
    {
        ValidateThresholdRange(req.MinValue, req.MaxValue);
        var entity = await _uow.AlertThresholds.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("AlertThreshold");
        await EnsureThresholdAreaAsync(entity, ct);
        entity.MinValue = req.MinValue;
        entity.MaxValue = req.MaxValue;
        entity.Severity = ParseSeverity(req.Severity);
        entity.IsActive = req.IsActive;
        _uow.AlertThresholds.Update(entity);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "UpdateAlertThreshold", "AlertThreshold", entity.Id, entity.SensorType, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<AlertThresholdDto>.Ok(MapThreshold(entity));
    }

    public async Task<ApiResponse> DeleteThresholdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _uow.AlertThresholds.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("AlertThreshold");
        await EnsureThresholdAreaAsync(entity, ct);
        _uow.AlertThresholds.Remove(entity);
        await ConfigAudit.WriteAsync(
            _uow, (await _areas.CurrentUserAsync(ct))?.Id,
            "DeleteAlertThreshold", "AlertThreshold", entity.Id, entity.SensorType, ct);
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
        if (farmingAreaId is Guid requestedArea)
            await _areas.EnsureAsync(requestedArea, manage: false, ct);

        IEnumerable<Alert> items;
        if (activeOnly == true)
            items = await _uow.Alerts.FindAsync(
                a => a.Status == AlertStatus.Active
                     || a.Status == AlertStatus.Acknowledged
                     || a.Status == AlertStatus.InProgress, ct);
        else
            items = await _uow.Alerts.GetAllAsync(ct);

        items = await FilterByFarmingAreaAsync(items, farmingAreaId, ct);
        items = await FilterToAssignedAreasAsync(items, ct);
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
        if (farmingAreaId is Guid historyArea)
            await _areas.EnsureAsync(historyArea, manage: false, ct);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var items = await _uow.Alerts.FindAsync(
            a => (a.Status == AlertStatus.Resolved
                    || a.Status == AlertStatus.Recovered)
                 && a.CreatedAt >= cutoff,
            ct);
        items = await FilterByFarmingAreaAsync(items, farmingAreaId, ct);
        items = await FilterToAssignedAreasAsync(items, ct);
        var dtos = await MapAlertsAsync(
            items.OrderByDescending(a => a.CreatedAt), ct);
        return ApiResponse<IEnumerable<AlertDto>>.Ok(dtos);
    }

    public async Task<ApiResponse<AlertDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        await EnsureAlertAreaAsync(alert, ct);
        var dto = (await MapAlertsAsync(new[] { alert }, ct)).First();
        return ApiResponse<AlertDto>.Ok(dto);
    }

    public async Task<ApiResponse<AlertUnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var active = await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct);
        active = await FilterToAssignedAreasAsync(active, ct);
        var count = active.Count();
        return ApiResponse<AlertUnreadCountDto>.Ok(new AlertUnreadCountDto(count, count));
    }

    public async Task<ApiResponse<AlertDto>> AcknowledgeAsync(
        Guid id, AcknowledgeAlertRequest req, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        await EnsureAlertAreaAsync(alert, ct);
        if (alert.Status is AlertStatus.Resolved or AlertStatus.Recovered)
            throw AppException.Conflict("Không thể acknowledge sự cố đã đóng.");
        if (alert.Status is AlertStatus.Acknowledged or AlertStatus.InProgress)
            return ApiResponse<AlertDto>.Ok(
                (await MapAlertsAsync(new[] { alert }, ct)).First());
        var actorId = (await _areas.CurrentUserAsync(ct))?.Id;
        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = actorId;
        _uow.Alerts.Update(alert);
        await ConfigAudit.WriteAsync(
            _uow, actorId, "AcknowledgeAlert", "Alert", alert.Id, alert.Message, ct);
        await _uow.SaveChangesAsync(ct);
        var dto = (await MapAlertsAsync(new[] { alert }, ct)).First();
        return ApiResponse<AlertDto>.Ok(dto);
    }

    public async Task<ApiResponse<AlertDto>> StartProcessingAsync(
        Guid id, StartProcessingRequest? req, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        await EnsureAlertAreaAsync(alert, ct);
        if (alert.Status is AlertStatus.Resolved or AlertStatus.Recovered)
            throw AppException.Conflict("Không thể xử lý lại sự cố đã đóng.");
        if (alert.Status == AlertStatus.InProgress)
            return ApiResponse<AlertDto>.Ok((await MapAlertsAsync(new[] { alert }, ct)).First());
        var actorId = (await _areas.CurrentUserAsync(ct))?.Id;
        alert.Status = AlertStatus.InProgress;
        alert.ProcessingStartedAt = DateTime.UtcNow;
        alert.ProcessingBy = actorId;
        if (alert.AcknowledgedAt is null)
        {
            alert.AcknowledgedAt = DateTime.UtcNow;
            alert.AcknowledgedBy = actorId;
        }
        _uow.Alerts.Update(alert);
        await ConfigAudit.WriteAsync(
            _uow, actorId, "StartProcessingAlert", "Alert", alert.Id, alert.Message, ct);
        await _uow.SaveChangesAsync(ct);
        var dto = (await MapAlertsAsync(new[] { alert }, ct)).First();
        return ApiResponse<AlertDto>.Ok(dto);
    }

    public async Task<ApiResponse<AlertDto>> ResolveAsync(
        Guid id, ResolveAlertRequest? req, CancellationToken ct = default)
    {
        var alert = await _uow.Alerts.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Alert");
        await EnsureAlertAreaAsync(alert, ct);
        if (!IsOpen(alert.Status))
            throw AppException.Conflict("Sự cố đã được đóng.");
        if (req?.DeviceRecovered == true
            && !await HasRecoveryEvidenceAsync(alert, ct))
            throw AppException.Conflict(
                "Không có bằng chứng telemetry phù hợp để xác nhận thiết bị đã phục hồi.");
        var actorId = (await _areas.CurrentUserAsync(ct))?.Id;
        alert.Status = req?.DeviceRecovered == true ? AlertStatus.Recovered : AlertStatus.Resolved;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolvedBy = actorId;
        alert.ResolutionReason = string.IsNullOrWhiteSpace(req?.Reason) ? null : req!.Reason!.Trim();
        alert.ResolutionAction = string.IsNullOrWhiteSpace(req?.Action) ? null : req!.Action!.Trim();
        alert.ResolutionNote = string.IsNullOrWhiteSpace(req?.Note) ? null : req!.Note!.Trim();
        _uow.Alerts.Update(alert);
        await ConfigAudit.WriteAsync(
            _uow, actorId, "ResolveAlert", "Alert", alert.Id, alert.Message, ct);
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
            var candidates = await _uow.AlertThresholds.FindAsync(
                t => t.IsActive && t.SensorType == sensor.SensorType, ct);
            var areaId = await AreaOfWaterSystemAsync(sensor.WaterSystemId, ct);
            var thr = PickThreshold(candidates, areaId, sensor.WaterSystemId);
            if (thr is null) return;
            min = thr.MinValue;
            max = thr.MaxValue;
            severity = thr.Severity;
            thresholdId = thr.Id;
        }

        var areaSnapshot = await AreaOfWaterSystemAsync(
            sensor.WaterSystemId, ct);
        if (value >= min && value <= max)
        {
            await ClearAsync(
                "threshold_violation",
                "Sensor",
                sensor.Id.ToString("N"),
                ct);
            return;
        }

        var existing = (await _uow.Alerts.FindAsync(
            a => a.SensorId == sensor.Id
                && a.IncidentType == "threshold_violation"
                && (a.Status == AlertStatus.Active
                    || a.Status == AlertStatus.Acknowledged
                    || a.Status == AlertStatus.InProgress), ct)).FirstOrDefault();
        if (existing is not null)
        {
            existing.OccurrenceCount = Math.Max(1, existing.OccurrenceCount) + 1;
            existing.LastOccurredAt = DateTime.UtcNow;
            existing.TriggerValue = value;
            _uow.Alerts.Update(existing);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        var msg = value < min
            ? $"{sensor.SensorType} thấp: {value} < {min} (sensor {sensor.SensorCode})"
            : $"{sensor.SensorType} cao: {value} > {max} (sensor {sensor.SensorCode})";

        var alert = new Alert
        {
            SensorId = sensor.Id,
            AlertThresholdId = thresholdId,
            FarmingAreaId = areaSnapshot,
            IncidentType = "threshold_violation",
            SourceType = "Sensor",
            SourceId = sensor.Id.ToString("N"),
            IncidentId = IncidentIdentity(
                areaSnapshot, "threshold_violation", "Sensor",
                sensor.Id.ToString("N")),
            Message = msg,
            Severity = severity,
            Status = AlertStatus.Active,
            TriggerValue = value,
            OccurrenceCount = 1,
            LastOccurredAt = DateTime.UtcNow
        };
        await _uow.Alerts.AddAsync(alert, ct);
        await _uow.SaveChangesAsync(ct);

        await _notifications.NotifyAsync(
            NotificationTypeCodes.SensorThreshold,
            alert.Message,
            alert.Id,
            areaSnapshot,
            severity.ToString(),
            new Dictionary<string, string?>
            {
                ["sensor"] = sensor.SensorType,
                ["code"] = sensor.SensorCode,
                ["value"] = value.ToString("0.##"),
                ["unit"] = sensor.Unit,
                ["min"] = min?.ToString("0.##"),
                ["max"] = max?.ToString("0.##")
            }, ct);
    }

    public async Task RaiseOnceAsync(
        string incidentType,
        string sourceType,
        string sourceId,
        string message,
        string? typeCode = null,
        Guid? areaId = null,
        IReadOnlyDictionary<string, string?>? tokens = null,
        CancellationToken ct = default)
    {
        ValidateIncidentIdentity(incidentType, sourceType, sourceId);
        var existing = (await _uow.Alerts.FindAsync(
            a => a.IncidentType == incidentType
                && a.SourceType == sourceType
                && a.SourceId == sourceId
                && a.FarmingAreaId == areaId
                && (a.Status == AlertStatus.Active
                    || a.Status == AlertStatus.Acknowledged
                    || a.Status == AlertStatus.InProgress), ct)).FirstOrDefault();
        if (existing is not null)
        {
            existing.OccurrenceCount = Math.Max(1, existing.OccurrenceCount) + 1;
            existing.LastOccurredAt = DateTime.UtcNow;
            _uow.Alerts.Update(existing);
            await _uow.SaveChangesAsync(ct);
            return;
        }

        var alert = new Alert
        {
            FarmingAreaId = areaId,
            IncidentType = incidentType,
            SourceType = sourceType,
            SourceId = sourceId,
            IncidentId = IncidentIdentity(
                areaId, incidentType, sourceType, sourceId),
            Message = message,
            Severity = AlertSeverity.Warning,
            Status = AlertStatus.Active,
            OccurrenceCount = 1,
            LastOccurredAt = DateTime.UtcNow
        };
        await _uow.Alerts.AddAsync(alert, ct);
        await _uow.SaveChangesAsync(ct);
        await _notifications.NotifyAsync(
            typeCode ?? NotificationTypeCodes.System, alert.Message, alert.Id, areaId,
            nameof(AlertSeverity.Warning), tokens, ct);
    }

    public async Task ClearAsync(
        string incidentType,
        string sourceType,
        string sourceId,
        CancellationToken ct = default)
    {
        ValidateIncidentIdentity(incidentType, sourceType, sourceId);
        var existing = (await _uow.Alerts.FindAsync(
            a => a.IncidentType == incidentType
                && a.SourceType == sourceType
                && a.SourceId == sourceId
                && (a.Status == AlertStatus.Active
                    || a.Status == AlertStatus.Acknowledged
                    || a.Status == AlertStatus.InProgress), ct)
            ?? []).FirstOrDefault();
        if (existing is null) return;
        existing.Status = AlertStatus.Recovered;
        existing.ResolvedAt = DateTime.UtcNow;
        existing.ResolutionReason = "Telemetry returned to a safe state.";
        _uow.Alerts.Update(existing);
        await _uow.SaveChangesAsync(ct);
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
            var areaId = await AreaOfWaterSystemAsync(
                sensor.WaterSystemId, ct);
            var existing = (await _uow.Alerts.FindAsync(
                a => a.SensorId == sensor.Id
                    && a.IncidentType == "sensor_disconnect"
                    && (a.Status == AlertStatus.Active
                        || a.Status == AlertStatus.Acknowledged
                        || a.Status == AlertStatus.InProgress), ct)).FirstOrDefault();
            if (existing is not null)
            {
                existing.OccurrenceCount = Math.Max(1, existing.OccurrenceCount) + 1;
                existing.LastOccurredAt = DateTime.UtcNow;
                _uow.Alerts.Update(existing);
                continue;
            }

            var alert = new Alert
            {
                SensorId = sensor.Id,
                FarmingAreaId = areaId,
                IncidentType = "sensor_disconnect",
                SourceType = "Sensor",
                SourceId = sensor.Id.ToString("N"),
                Message = $"Cảm biến mất kết nối: {sensor.SensorCode} (>{timeoutMinutes} phút)",
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.Active,
                OccurrenceCount = 1,
                LastOccurredAt = DateTime.UtcNow,
                IncidentId = IncidentIdentity(
                    areaId, "sensor_disconnect", "Sensor",
                    sensor.Id.ToString("N"))
            };
            await _uow.Alerts.AddAsync(alert, ct);
            created++;
            await _notifications.NotifyAsync(
                NotificationTypeCodes.SensorDisconnect, alert.Message, alert.Id,
                areaId,
                nameof(AlertSeverity.Critical),
                new Dictionary<string, string?>
                {
                    ["sensor"] = sensor.SensorType,
                    ["code"] = sensor.SensorCode,
                    ["name"] = sensor.SensorType,
                    ["minutes"] = timeoutMinutes.ToString()
                }, ct);
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
            var existing = (await _uow.Alerts.FindAsync(
                a => a.IncidentType == "device_disconnect"
                    && a.SourceType == "Device"
                    && a.SourceId == device.Id.ToString("N")
                    && (a.Status == AlertStatus.Active
                        || a.Status == AlertStatus.Acknowledged
                        || a.Status == AlertStatus.InProgress), ct)).FirstOrDefault();
            if (existing is not null)
            {
                existing.OccurrenceCount = Math.Max(1, existing.OccurrenceCount) + 1;
                existing.LastOccurredAt = DateTime.UtcNow;
                _uow.Alerts.Update(existing);
                continue;
            }

            var alert = new Alert
            {
                FarmingAreaId = device.FarmingAreaId,
                IncidentType = "device_disconnect",
                SourceType = "Device",
                SourceId = device.Id.ToString("N"),
                Message = msg,
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.Active,
                OccurrenceCount = 1,
                LastOccurredAt = DateTime.UtcNow,
                IncidentId = IncidentIdentity(
                    device.FarmingAreaId, "device_disconnect", "Device",
                    device.Id.ToString("N"))
            };
            await _uow.Alerts.AddAsync(alert, ct);
            created++;
            await _notifications.NotifyAsync(
                NotificationTypeCodes.DeviceDisconnect, alert.Message, alert.Id,
                device.FarmingAreaId,
                nameof(AlertSeverity.Critical),
                new Dictionary<string, string?>
                {
                    ["code"] = device.DeviceCode,
                    ["name"] = device.Name ?? device.DeviceCode,
                    ["deviceType"] = kind,
                    ["minutes"] = timeoutMinutes.ToString()
                }, ct);
        }

        var kioskCutoff = DateTime.UtcNow.AddMinutes(-2);
        var kiosks = await _uow.FarmKiosks.FindAsync(
            k => k.Status != "Revoked" && k.Status != "Created", ct);
        foreach (var kiosk in kiosks)
        {
            var message = $"Kiosk mất kết nối: {kiosk.Code} (>2 phút)";
            var active = (await _uow.Alerts.FindAsync(
                a => a.IncidentType == "kiosk_disconnect"
                    && a.SourceType == "Kiosk"
                    && a.SourceId == kiosk.Id.ToString("N")
                    && (a.Status == AlertStatus.Active
                        || a.Status == AlertStatus.Acknowledged
                        || a.Status == AlertStatus.InProgress), ct))
                .FirstOrDefault();

            if (kiosk.LastSeenAt is not null && kiosk.LastSeenAt >= kioskCutoff)
            {
                if (active is not null)
                {
                    active.Status = AlertStatus.Recovered;
                    active.ResolvedAt = DateTime.UtcNow;
                    active.ResolutionReason =
                        "Kiosk heartbeat recovered.";
                    _uow.Alerts.Update(active);
                }
                continue;
            }

            if (kiosk.LastSeenAt is null || kiosk.LastSeenAt < kioskCutoff)
            {
                if (active is null)
                {
                    var alert = new Alert
                    {
                        FarmingAreaId = kiosk.FarmingAreaId,
                        IncidentType = "kiosk_disconnect",
                        SourceType = "Kiosk",
                        SourceId = kiosk.Id.ToString("N"),
                        Message = message,
                        Severity = AlertSeverity.Critical,
                        Status = AlertStatus.Active,
                        OccurrenceCount = 1,
                        LastOccurredAt = DateTime.UtcNow,
                        IncidentId = IncidentIdentity(
                            kiosk.FarmingAreaId, "kiosk_disconnect",
                            "Kiosk", kiosk.Id.ToString("N"))
                    };
                    await _uow.Alerts.AddAsync(alert, ct);
                    created++;
                    await _notifications.NotifyAsync(
                        NotificationTypeCodes.KioskDisconnect, alert.Message, alert.Id,
                        kiosk.FarmingAreaId,
                        nameof(AlertSeverity.Critical),
                        new Dictionary<string, string?>
                        {
                            ["code"] = kiosk.Code,
                            ["name"] = kiosk.Name ?? kiosk.Code,
                            ["minutes"] = "2"
                        }, ct);
                }
            }
        }

        var openDisconnects = await _uow.Alerts.FindAsync(
            alert => (alert.IncidentType == "sensor_disconnect"
                      || alert.IncidentType == "device_disconnect")
                     && (alert.Status == AlertStatus.Active
                         || alert.Status == AlertStatus.Acknowledged
                         || alert.Status == AlertStatus.InProgress),
            ct);
        foreach (var alert in openDisconnects)
        {
            if (!await HasRecoveryEvidenceAsync(alert, ct))
                continue;
            alert.Status = AlertStatus.Recovered;
            alert.ResolvedAt = DateTime.UtcNow;
            alert.ResolutionReason = "Heartbeat recovered.";
            _uow.Alerts.Update(alert);
        }

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

        return items.Where(a =>
            a.FarmingAreaId == areaId
            || (a.FarmingAreaId is null
                && a.SensorId is Guid sid
                && sensorIds.Contains(sid)));
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
        areaIds.AddRange(list
            .Where(alert => alert.FarmingAreaId.HasValue)
            .Select(alert => alert.FarmingAreaId!.Value)
            .Distinct()
            .Where(id => !areaIds.Contains(id)));
        var areas = areaIds.Count == 0
            ? new List<FarmingArea>()
            : (await _uow.FarmingAreas.FindAsync(a => areaIds.Contains(a.Id), ct)).ToList();
        var areaMap = areas.ToDictionary(a => a.Id);

        var thresholds = thresholdIds.Count == 0
            ? new List<AlertThreshold>()
            : (await _uow.AlertThresholds.FindAsync(t => thresholdIds.Contains(t.Id), ct)).ToList();
        var thrMap = thresholds.ToDictionary(t => t.Id);

        var typeThresholds = (await _uow.AlertThresholds.FindAsync(
                t => t.IsActive, ct))
            .ToList();

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
            var snapshotAreaId = a.FarmingAreaId ?? ws?.FarmingAreaId;
            if (snapshotAreaId is Guid aid)
                areaMap.TryGetValue(aid, out area);

            AlertThreshold? thr = null;
            if (a.AlertThresholdId is Guid tid)
                thrMap.TryGetValue(tid, out thr);
            if (thr is null && sensor is not null)
                thr = PickThreshold(
                    typeThresholds.Where(candidate =>
                        string.Equals(
                            candidate.SensorType,
                            sensor.SensorType,
                            StringComparison.OrdinalIgnoreCase)),
                    snapshotAreaId,
                    sensor.WaterSystemId);

            var min = sensor?.MinThreshold ?? thr?.MinValue;
            var max = sensor?.MaxThreshold ?? thr?.MaxValue;
            var sensorType = sensor?.SensorType ?? InferSensorType(a.Message);
            var category = InferCategory(a.Message, sensorType);
            var title = BuildTitle(a.Message, sensorType);
            var (score, explanation, sla) = ComputePriority(a, min, max);
            var (aiTip, confidence) = BuildAiTip(category, sensorType, a, min, max);
            var location = area?.Name
                ?? (sensor is null ? "Hệ thống" : sensor.SensorCode);
            var kind = InferKind(a.Message, category, sensorType);
            var deviceCode = sensor?.SensorCode ?? ExtractDeviceCode(a.Message);

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
                snapshotAreaId,
                area?.Name,
                location,
                min,
                max,
                score,
                explanation,
                sla,
                aiTip,
                confidence,
                a.AcknowledgedBy,
                FriendlyMessage(a.Message),
                SourceLabelOf(kind),
                kind,
                deviceCode,
                area?.Code,
                a.ProcessingStartedAt,
                a.ResolvedAt,
                a.ResolutionReason,
                a.ResolutionAction,
                a.ResolutionNote,
                a.OccurrenceCount < 1 ? 1 : a.OccurrenceCount,
                a.LastOccurredAt ?? a.CreatedAt,
                a.IncidentId));
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

    private static string InferKind(string message, string category, string? sensorType)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("no2") || m.Contains("nh3") || m.Contains("no3") || m.Contains("phân tích"))
            return "waterAnalysis";
        if (m.Contains("camera") || string.Equals(sensorType, "Camera", StringComparison.OrdinalIgnoreCase))
            return "camera";
        if (m.Contains("bơm") || m.Contains("pump") || m.Contains("van") || m.Contains("ras"))
            return "ras";
        if (m.Contains("cua") || m.Contains("hộp") || m.Contains("crab"))
            return "crab";
        if (m.Contains("controller") || m.Contains("esp32") || m.Contains("crabsense-c")
            || m.Contains("heartbeat"))
            return "controller";
        if (category == "Device" && (m.Contains("mất kết nối") || m.Contains("offline")))
            return m.Contains("camera") ? "camera" : "controller";
        if (category == "WaterQuality") return "sensor";
        return "system";
    }

    private static string SourceLabelOf(string kind) => kind switch
    {
        "controller" => "Controller Monitor",
        "camera" => "Camera AI",
        "sensor" => "Realtime Sensor",
        "ras" => "RAS",
        "waterAnalysis" => "Water Analysis",
        "crab" => "Crab AI",
        _ => "System"
    };

    private static string? ExtractDeviceCode(string message)
    {
        var parts = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Contains('-') && p.Length >= 5 && p.Any(char.IsDigit))
                return p.Trim(',', '.', ';');
        }
        return null;
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
        var mapped = FriendlyMessage(message);
        var cut = mapped.Split('(')[0].Trim();
        if (cut.Length > 72) cut = cut[..69] + "...";
        return cut;
    }

    private static string FriendlyMessage(string message)
    {
        var key = message.Trim().ToLowerInvariant();
        return key switch
        {
            "realtime_sensor" => "Cảm biến vượt ngưỡng",
            "controller_disconnect" => "Controller mất kết nối",
            "sensor_timeout" => "Sensor không gửi dữ liệu",
            "ras_component_error" => "Thiết bị RAS lỗi",
            _ when key.Contains("realtime_sensor") => "Cảm biến vượt ngưỡng",
            _ when key.Contains("controller_disconnect") => "Controller mất kết nối",
            _ when key.Contains("sensor_timeout") => "Sensor không gửi dữ liệu",
            _ when key.Contains("ras_component_error") => "Thiết bị RAS lỗi",
            _ => message
        };
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

    /// <summary>Khu của cảm biến: Sensor → WaterSystem → FarmingArea.</summary>
    private async Task<Guid?> AreaOfWaterSystemAsync(Guid? waterSystemId, CancellationToken ct)
    {
        if (waterSystemId is not Guid id || id == Guid.Empty) return null;
        var ws = await _uow.WaterSystems.GetByIdAsync(id, ct);
        return ws?.FarmingAreaId;
    }

    private static AlertSeverity ParseSeverity(string? s) =>
        Enum.TryParse<AlertSeverity>(s, true, out var v) ? v : AlertSeverity.Warning;

    private async Task<Guid?> RequireThresholdAreaAsync(Guid? farmingAreaId, CancellationToken ct)
    {
        var user = await _areas.CurrentUserAsync(ct);
        if (user is null)
            return farmingAreaId is Guid id && id != Guid.Empty ? id : null;
        if (farmingAreaId is not Guid areaId || areaId == Guid.Empty)
            throw AppException.BadRequest("Ngưỡng môi trường phải gắn với một khu nuôi.");
        await _areas.EnsureAsync(areaId, manage: true, ct);
        return areaId;
    }

    private async Task<Guid?> RequireWaterSystemAsync(Guid? waterSystemId, Guid? areaId, CancellationToken ct)
    {
        if (waterSystemId is not Guid id || id == Guid.Empty) return null;
        var system = await _uow.WaterSystems.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("WaterSystem");
        if (areaId is Guid area && system.FarmingAreaId != area)
            throw AppException.BadRequest("Hệ thống nước không thuộc khu này.");
        if (system.FarmingAreaId is Guid systemArea)
            await _areas.EnsureAsync(systemArea, manage: true, ct);
        return id;
    }

    private async Task EnsureThresholdAreaAsync(AlertThreshold entity, CancellationToken ct)
    {
        if (entity.FarmingAreaId is not Guid areaId || areaId == Guid.Empty)
            throw AppException.Forbidden("Ngưỡng chưa gắn khu nuôi.");
        await _areas.EnsureAsync(areaId, manage: true, ct);
    }

    private async Task EnsureAlertAreaAsync(Alert alert, CancellationToken ct)
    {
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is null) return;
        var areas = await AreaByAlertAsync(new[] { alert }, ct);
        if (!areas.TryGetValue(alert.Id, out var areaId) || areaId is not Guid id || !scope.Contains(id))
            throw AppException.Forbidden("Cảnh báo không thuộc khu được phân công.");
    }

    private async Task<IEnumerable<Alert>> FilterToAssignedAreasAsync(IEnumerable<Alert> items, CancellationToken ct)
    {
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is null) return items;
        var list = items.ToList();
        var areas = await AreaByAlertAsync(list, ct);
        return list.Where(alert =>
            areas.TryGetValue(alert.Id, out var areaId)
            && areaId is Guid id
            && scope.Contains(id));
    }

    private async Task<Dictionary<Guid, Guid?>> AreaByAlertAsync(IReadOnlyCollection<Alert> alerts, CancellationToken ct)
    {
        var sensorIds = alerts.Where(alert => alert.SensorId is Guid).Select(alert => alert.SensorId!.Value).Distinct().ToList();
        var sensors = sensorIds.Count == 0
            ? new List<Sensor>()
            : (await _uow.Sensors.FindAsync(sensor => sensorIds.Contains(sensor.Id), ct)).ToList();
        var sensorMap = sensors.ToDictionary(sensor => sensor.Id);
        var systemIds = sensors.Where(sensor => sensor.WaterSystemId is Guid).Select(sensor => sensor.WaterSystemId!.Value).Distinct().ToList();
        var systems = systemIds.Count == 0
            ? new List<WaterSystem>()
            : (await _uow.WaterSystems.FindAsync(system => systemIds.Contains(system.Id), ct)).ToList();
        var areaBySystem = systems.ToDictionary(system => system.Id, system => system.FarmingAreaId);

        var result = new Dictionary<Guid, Guid?>();
        foreach (var alert in alerts)
        {
            Guid? area = alert.FarmingAreaId;
            if (area is null
                && alert.SensorId is Guid sensorId
                && sensorMap.TryGetValue(sensorId, out var sensor)
                && sensor.WaterSystemId is Guid systemId
                && areaBySystem.TryGetValue(systemId, out var found))
                area = found;
            result[alert.Id] = area;
        }
        return result;
    }

    private async Task<bool> HasRecoveryEvidenceAsync(
        Alert alert,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        if (alert.IncidentType == "sensor_disconnect"
            && alert.SensorId is Guid sensorId)
        {
            var sensor = await _uow.Sensors.GetByIdAsync(sensorId, ct);
            return sensor?.IsActive == true
                && sensor.LastSeenAt >= cutoff;
        }
        if (alert.IncidentType == "device_disconnect"
            && Guid.TryParse(alert.SourceId, out var deviceId))
        {
            var device = await _uow.Devices.GetByIdAsync(deviceId, ct);
            return device?.Status == DeviceStatus.Online
                && device.LastSeenAt >= cutoff
                && device.EdgeState is "Approved" or "Online";
        }
        if (alert.IncidentType == "kiosk_disconnect"
            && Guid.TryParse(alert.SourceId, out var kioskId))
        {
            var kiosk = await _uow.FarmKiosks.GetByIdAsync(kioskId, ct);
            return kiosk?.LastSeenAt >= DateTime.UtcNow.AddMinutes(-2);
        }
        if (alert.IncidentType == "threshold_violation"
            && alert.SensorId is Guid thresholdSensorId)
        {
            var sensor = await _uow.Sensors.GetByIdAsync(
                thresholdSensorId, ct);
            if (sensor is null)
                return false;
            var threshold = alert.AlertThresholdId is Guid thresholdId
                ? await _uow.AlertThresholds.GetByIdAsync(thresholdId, ct)
                : null;
            var min = sensor.MinThreshold ?? threshold?.MinValue;
            var max = sensor.MaxThreshold ?? threshold?.MaxValue;
            if (min is null || max is null)
                return false;
            var latest = (await _uow.WaterMeasurements.FindAsync(
                    measurement => measurement.SensorId == sensor.Id, ct))
                .OrderByDescending(measurement => measurement.MeasuredAt)
                .FirstOrDefault();
            return latest is not null
                && latest.MeasuredAt >= cutoff
                && latest.MeasuredAt <= DateTime.UtcNow.AddSeconds(5)
                && latest.Value >= min
                && latest.Value <= max;
        }
        return false;
    }

    private static bool IsOpen(AlertStatus status) =>
        status is AlertStatus.Active
            or AlertStatus.Acknowledged
            or AlertStatus.InProgress;

    private static void ValidateIncidentIdentity(
        string incidentType,
        string sourceType,
        string sourceId)
    {
        if (string.IsNullOrWhiteSpace(incidentType)
            || string.IsNullOrWhiteSpace(sourceType)
            || string.IsNullOrWhiteSpace(sourceId))
            throw AppException.BadRequest(
                "IncidentType, SourceType và SourceId là bắt buộc.");
    }

    private static string IncidentIdentity(
        Guid? areaId,
        string incidentType,
        string sourceType,
        string sourceId) =>
        $"INC:{areaId?.ToString("N") ?? "unknown"}:"
        + $"{incidentType.Trim().ToLowerInvariant()}:"
        + $"{sourceType.Trim().ToLowerInvariant()}:"
        + sourceId.Trim().ToLowerInvariant();

    private static AlertThreshold? PickThreshold(
        IEnumerable<AlertThreshold> candidates, Guid? areaId, Guid? waterSystemId)
    {
        var list = candidates
            .Where(item => item.IsActive && item.MinValue < item.MaxValue)
            .ToList();
        if (waterSystemId is Guid systemId)
        {
            var bySystem = list.FirstOrDefault(item => item.WaterSystemId == systemId);
            if (bySystem is not null) return bySystem;
        }
        if (areaId is Guid area)
        {
            var byArea = list.FirstOrDefault(item => item.FarmingAreaId == area && item.WaterSystemId is null);
            if (byArea is not null) return byArea;
        }
        return list.FirstOrDefault(item =>
            item.FarmingAreaId is null
            && item.WaterSystemId is null);
    }

    private static AlertThresholdDto MapThreshold(AlertThreshold t) =>
        new(t.Id, t.SensorType, t.MinValue, t.MaxValue, t.Severity.ToString(), t.IsActive,
            t.FarmingAreaId, t.WaterSystemId);

    private static void ValidateThresholdRange(decimal minValue, decimal maxValue)
    {
        if (minValue >= maxValue)
            throw AppException.BadRequest("MinValue must be less than MaxValue.");
    }
}
