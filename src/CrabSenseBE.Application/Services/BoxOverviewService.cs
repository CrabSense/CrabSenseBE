using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>
/// Aggregates farming status + area IoT + alerts into one Mobile Boxes payload.
/// Water/devices are area-scoped (RAS); unverified assessments are unavailable.
/// </summary>
public class BoxOverviewService : IBoxOverviewService
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;

    public BoxOverviewService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<BoxesOverviewResponseDto>> GetOverviewAsync(
        Guid? farmingAreaId = null,
        Guid? farmingRowId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        if (farmingAreaId is Guid requestedAreaId && requestedAreaId != Guid.Empty)
            await _areas.EnsureAsync(requestedAreaId, manage: false, ct);
        if (farmingRowId is Guid requestedRowId && requestedRowId != Guid.Empty)
            await _areas.EnsureRowAsync(requestedRowId, manage: false, ct);

        var rows = (await _uow.FarmingRows.GetAllAsync(ct)).ToList();
        var rowMap = rows.ToDictionary(r => r.Id);
        var areas = (await _uow.FarmingAreas.GetAllAsync(ct)).ToDictionary(a => a.Id);

        var boxes = (await _uow.Boxes.GetAllAsync(ct)).AsEnumerable();
        var scope = await _areas.AreaIdsAsync(ct);
        if (scope is not null)
        {
            var allowedRows = rows.Where(row => scope.Contains(row.FarmingAreaId)).Select(row => row.Id).ToHashSet();
            boxes = boxes.Where(box => allowedRows.Contains(box.FarmingRowId));
        }
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

        // Only an explicit area (or explicit row) may populate the header.
        // An All Areas request must never present the first area's RAS as global.
        Guid? scopeAreaId = farmingAreaId is Guid selected && selected != Guid.Empty
            ? selected
            : farmingRowId is Guid selectedRow && rowMap.TryGetValue(selectedRow, out var filterRow)
                && (scope is null || scope.Contains(filterRow.FarmingAreaId))
                ? filterRow.FarmingAreaId : null;
        var requiredAreas = boxList
            .Where(box => rowMap.ContainsKey(box.FarmingRowId))
            .Select(box => rowMap[box.FarmingRowId].FarmingAreaId)
            .Where(id => id != Guid.Empty && (scope is null || scope.Contains(id)))
            .ToHashSet();
        if (scopeAreaId is Guid headerArea && (scope is null || scope.Contains(headerArea)))
            requiredAreas.Add(headerArea);
        var areaIoT = await LoadAreaIoTAsync(requiredAreas, rowMap, ct);
        var empty = new AreaIoT(new BoxWaterSnapshotDto(null, null, null, null, []),
            new BoxDeviceSnapshotDto(false, 0, 0), []);
        var header = scopeAreaId is Guid aid ? areaIoT.GetValueOrDefault(aid, empty) : empty;
        var farmName = scopeAreaId is Guid namedArea && areas.TryGetValue(namedArea, out var areaEntity)
            ? areaEntity.Name : "Trang trại";

        // Crabs currently in these boxes: Crab.BoxId or open allocation
        var liveCrabs = (await _uow.Crabs.GetAllAsync(ct)).Where(IsLiveCrab).ToList();
        var liveIds = liveCrabs.Select(c => c.Id).ToHashSet();
        var openAllocsForCrabs = liveIds.Count == 0
            ? new List<CrabBoxAllocation>()
            : (await _uow.CrabBoxAllocations.FindAsync(
                a => a.EndTime == null && liveIds.Contains(a.CrabId), ct)).ToList();
        var allocBoxByCrab = openAllocsForCrabs
            .GroupBy(a => a.CrabId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.StartTime).First().BoxId);
        var crabsByBox = new Dictionary<Guid, List<Crab>>();
        foreach (var crab in liveCrabs)
        {
            var boxId = crab.BoxId is Guid snap && snap != Guid.Empty
                ? snap
                : allocBoxByCrab.GetValueOrDefault(crab.Id);
            if (boxId == Guid.Empty || !boxIds.Contains(boxId)) continue;
            if (!crabsByBox.TryGetValue(boxId, out var list))
            {
                list = [];
                crabsByBox[boxId] = list;
            }
            list.Add(crab);
        }

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

        var lastOpByBox = new Dictionary<Guid, DateTime>();
        foreach (var op in await _uow.FarmOperations.GetAllAsync(ct))
        {
            foreach (var raw in JsonStringList.Parse(op.BoxIdsJson))
            {
                if (!Guid.TryParse(raw, out var bid) || !boxIds.Contains(bid)) continue;
                if (!lastOpByBox.TryGetValue(bid, out var prev) || op.Timestamp > prev)
                    lastOpByBox[bid] = op.Timestamp;
            }
        }

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
            var alive = boxCrabs.Where(IsLiveCrab).ToList();
            var primary = alive.OrderByDescending(c => c.CreatedAt).FirstOrDefault();

            string? batch = null;
            string? crabType = null;
            if (primary?.CrabLotId is Guid lotId && lots.TryGetValue(lotId, out var lot))
            {
                batch = lot.LotCode;
                crabType = string.IsNullOrWhiteSpace(lot.SupplierName) ? "Cua lột" : lot.SupplierName;
            }

            openAllocs.TryGetValue(box.Id, out var alloc);
            lastMoltByBox.TryGetValue(box.Id, out var lastMolt);

            var occupied = alive.Count > 0;
            var boxStatus = occupied
                            || string.Equals(box.Status, BoxStatuses.Maintenance, StringComparison.OrdinalIgnoreCase)
                ? box.Status
                : BoxStatuses.Empty;

            var io = areaIoT.GetValueOrDefault(areaId, empty);
            var water = io.Water;
            var deviceSnap = io.Devices;
            var alerts = io.Alerts;
            // No verified assessment or model provenance is available on this route.
            // Preserve operational status, crab condition and scoped alerts separately.
            const string healthStatus = "unknown";
            var health = new BoxHealthScoreDto(null, null, null, "Unknown", "unknown",
                "No verified health or AI assessment available.");
            layoutIndex.TryGetValue(box.Id, out var layout);
            layout ??= new BoxMapLayoutDto(0.5, 0.5, 0, 0);

            var alertCount = CountRelevantAlerts(box, alerts);
            var latestAlert = alerts
                .Where(a => IsAlertRelevant(box, a))
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefault();

            DateTime? lastOp = lastOpByBox.TryGetValue(box.Id, out var opAt) ? opAt : null;
            DateTime? crabTouch = alive.Count == 0
                ? null
                : alive.Max(c => c.UpdatedAt ?? c.CreatedAt);
            // Lần cập nhật hộp = phiếu chăm sóc / tình trạng cua, không lấy
            // sensor khu (mọi hộp cùng một mốc đo nước).
            var lastUpdated = LatestRecordedUpdate(DateTime.UtcNow,
                box.UpdatedAt, box.CreatedAt, lastMolt?.MoltTime,
                alloc?.StartTime, crabTouch, lastOp);

            DateTime? harvestAt = null;
            if (occupied && string.Equals(boxStatus, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase))
                harvestAt = DateTime.UtcNow.AddHours(6);
            else if (occupied && alloc?.StartTime is DateTime started)
                harvestAt = started.AddDays(21);

            var waterTestDue = water.Alarms.Count > 0
                || (water.MeasuredAt is DateTime wm && wm < DateTime.UtcNow.AddHours(-12));
            var videoDue = occupied
                && (string.Equals(boxStatus, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)
                    || lastMolt is null);

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
                Status: boxStatus,
                IsOccupied: occupied,
                HealthStatus: healthStatus,
                Health: health,
                CrabCount: alive.Count,
                CrabType: crabType,
                Batch: batch,
                MoltingStage: primary?.MoltingStage,
                Water: water,
                Devices: deviceSnap,
                AlertCount: alertCount,
                LatestAlertTitle: latestAlert?.Message,
                LatestAlertAt: latestAlert?.CreatedAt,
                AiRecommendation: null,
                Layout: layout,
                LastUpdated: lastUpdated,
                ExpectedHarvestAt: harvestAt,
                WaterTestDue: waterTestDue,
                VideoDue: videoDue,
                Priority: null,
                CrabCondition: WorstCrabCondition(boxCrabs)));
        }

        var summary = new BoxesFarmSummaryDto(
            Total: items.Count,
            Healthy: null,
            Warning: null,
            Critical: null,
            Offline: scopeAreaId.HasValue && header.Devices.TotalCount > 0
                ? items.Count(i => i.Devices.TotalCount > 0 && !i.Devices.IsOnline) : null,
            WithAiRecommendation: null);

        return ApiResponse<BoxesOverviewResponseDto>.Ok(new BoxesOverviewResponseDto(
            FarmingAreaId: scopeAreaId,
            FarmName: farmName,
            Summary: summary,
            AreaWater: header.Water,
            AreaDevices: header.Devices,
            Items: items,
            SyncedAt: DateTime.UtcNow));
    }

    private sealed record AreaIoT(BoxWaterSnapshotDto Water, BoxDeviceSnapshotDto Devices,
        List<Alert> Alerts);

    private async Task<Dictionary<Guid, AreaIoT>> LoadAreaIoTAsync(
        HashSet<Guid> requiredAreas, Dictionary<Guid, FarmingRow> rowMap, CancellationToken ct)
    {
        var result = new Dictionary<Guid, AreaIoT>();
        if (requiredAreas.Count == 0) return result;

        var allSensors = (await _uow.Sensors.GetAllAsync(ct)).ToList();
        var allDevices = (await _uow.Devices.GetAllAsync(ct)).ToDictionary(d => d.Id);
        var systems = (await _uow.WaterSystems.GetAllAsync(ct)).ToDictionary(w => w.Id);
        var components = (await _uow.RasComponents.GetAllAsync(ct)).ToDictionary(c => c.Id);
        var allMeas = (await _uow.WaterMeasurements.GetAllAsync(ct)).ToList();
        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();
        var locations = new Dictionary<Guid, SensorHistoryLocation>();
        foreach (var sensor in allSensors)
        {
            var device = sensor.DeviceId is Guid did ? allDevices.GetValueOrDefault(did) : null;
            var location = await SensorHistoryScope.ResolveFromSnapshotAsync(sensor, device,
                id => rowMap.GetValueOrDefault(id)?.FarmingAreaId,
                id => systems.GetValueOrDefault(id)?.FarmingAreaId,
                id => components.GetValueOrDefault(id)?.WaterSystemId);
            if (location is not null) locations[sensor.Id] = location;
        }

        foreach (var area in requiredAreas)
        {
            var sensors = allSensors.Where(s => locations.GetValueOrDefault(s.Id)?.AreaId == area).ToList();
            var sensorIds = sensors.Select(s => s.Id).ToHashSet();
            // Explicit area/row configuration only; conflicting or unassigned devices
            // never inherit the global inventory through an empty association.
            var devices = allDevices.Values.Where(d =>
                (d.FarmingAreaId == area || (d.FarmingAreaId is null && d.FarmingRowId is Guid rowId
                    && rowMap.GetValueOrDefault(rowId)?.FarmingAreaId == area))
                && (d.FarmingRowId is null || rowMap.GetValueOrDefault(d.FarmingRowId.Value)?.FarmingAreaId == area))
                .ToList();
            var meas = allMeas.Where(m => sensorIds.Contains(m.SensorId)).ToList();
            // Use the captured area; do not reassign old alerts if a sensor moved.
            var alerts = activeAlerts.Where(a => a.FarmingAreaId == area).ToList();
            var water = BuildWaterSnapshot(sensors, meas, locations, area);
            var deviceSnap = BuildDeviceSnapshot(devices);
            result[area] = new AreaIoT(water, deviceSnap, alerts);
        }
        return result;
    }

    private static BoxWaterSnapshotDto BuildWaterSnapshot(
        List<Sensor> sensors, List<WaterMeasurement> measurements,
        Dictionary<Guid, SensorHistoryLocation> locations, Guid area)
    {
        // The legacy scalar contract cannot represent multiple sources of one
        // metric. Only a unique qualified sensor can populate each scalar.
        var now = DateTime.UtcNow;
        var latestBySensor = measurements.GroupBy(m => m.SensorId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MeasuredAt).First());
        var qualified = new List<(string Kind, WaterMeasurement Measurement)>();
        var alarms = new List<string>();
        foreach (var sensor in sensors.Where(s => s.IsActive))
        {
            if (!latestBySensor.TryGetValue(sensor.Id, out var latest)
                || !locations.TryGetValue(sensor.Id, out var location)
                || latest.ObservedAreaId != area || latest.ObservedRowId != location.RowId
                || latest.ObservedSensorType != sensor.SensorType || latest.DataQuality != "Realtime"
                || latest.ReceivedAt is not DateTime received || received < latest.MeasuredAt
                || received > now || latest.MeasuredAt > now
                || now - received > TimeSpan.FromMinutes(5) || now - latest.MeasuredAt > TimeSpan.FromMinutes(5)
                || string.IsNullOrWhiteSpace(sensor.Unit) || latest.Unit != sensor.Unit) continue;
            var kind = (sensor.SensorType?.Trim().ToLowerInvariant(), latest.Unit) switch
            {
                ("temperature", "°C") => "temperature",
                ("ph", "pH") => "ph",
                ("do" or "dissolvedoxygen" or "dissolved_oxygen", "mg/L") => "do",
                _ => ""
            };
            if (kind.Length == 0) continue;
            qualified.Add((kind, latest));
            if (sensor.MinThreshold is decimal min && sensor.MaxThreshold is decimal max && min >= max)
                continue;
            if (sensor.MinThreshold is decimal lower && latest.Value < lower)
                alarms.Add($"{sensor.SensorType} below_min");
            else if (sensor.MaxThreshold is decimal upper && latest.Value > upper)
                alarms.Add($"{sensor.SensorType} above_max");
        }
        var unique = qualified.GroupBy(p => p.Kind).Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Measurement);
        decimal? Value(string kind) => unique.TryGetValue(kind, out var m) ? m.Value : null;
        // One timestamp cannot describe different samples. Leave it absent
        // unless all reported scalars actually have the same measured time.
        var stamps = unique.Values.Select(m => m.MeasuredAt).Distinct().ToList();
        return new BoxWaterSnapshotDto(Value("temperature"), Value("ph"), Value("do"),
            stamps.Count == 1 ? stamps[0] : null, alarms);
    }

    private static BoxDeviceSnapshotDto BuildDeviceSnapshot(List<Device> devices)
    {
        if (devices.Count == 0)
            return new BoxDeviceSnapshotDto(false, 0, 0);
        var online = devices.Count(d => d.Status == DeviceStatus.Online);
        return new BoxDeviceSnapshotDto(online > 0, online, devices.Count);
    }

    private static DateTime? LatestRecordedUpdate(DateTime now, params DateTime?[] timestamps)
    {
        // Persisted timestamps are UTC; reject default and future values.
        // Unspecified kind is not accepted as a verified instant.
        return timestamps.Where(t => t.HasValue && t.Value.Kind == DateTimeKind.Utc
                && t.Value.Year >= 2000 && t.Value <= now)
            .Max();
    }

    /// <summary>
    /// Tình trạng đáng chú ý nhất trong số cua đang ở hộp — để màn Boxes tô màu
    /// theo đúng thứ nông dân đánh dấu hằng ngày, không chỉ theo điểm sức khỏe.
    /// Trả về key API (khớp CrabConditions.ToApi) hoặc null nếu hộp chưa có cua.
    /// Cua chết / đã bán không tính: BE tự trả hộp về trống, nên hộp không tô màu
    /// theo cua đã rời hộp (chỉ còn lại khi dữ liệu cũ chưa được trả hộp).
    /// </summary>
    private static string? WorstCrabCondition(IReadOnlyList<Crab> crabs)
    {
        CrabCondition? worst = null;
        var worstRank = -1;
        foreach (var crab in crabs)
        {
            var rank = crab.Condition switch
            {
                CrabCondition.Problem => 5,
                CrabCondition.Weak => 4,
                CrabCondition.Molting => 3,
                CrabCondition.Softshell => 2,
                CrabCondition.Premolt => 1,
                CrabCondition.Dead or CrabCondition.Harvested or CrabCondition.Sold => -1,
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

    private static bool IsLiveCrab(Crab c) =>
        c.Status is CrabStatus.Alive or CrabStatus.Molting or CrabStatus.Quarantined;
}
