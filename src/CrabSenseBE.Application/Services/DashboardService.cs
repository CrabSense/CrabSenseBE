using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Dashboard;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;

    public DashboardService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<DashboardOverviewDto>> GetOverviewAsync(
        Guid? farmingAreaId = null, CancellationToken ct = default)
    {
        var scope = await ResolveScopeAsync(farmingAreaId, ct);
        var boxes = scope.Boxes;
        var crabs = (await _uow.Crabs.GetAllAsync(ct))
            .Where(c => c.BoxId is Guid bid && scope.BoxIds.Contains(bid))
            .ToList();
        var alerts = await FilterAlertsAsync(scope, ct);
        var devices = scope.Devices;

        var totalBoxes = boxes.Count;
        var activeBoxes = boxes.Count(b =>
            string.Equals(b.Status, BoxStatuses.Active, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)
            || b.IsOccupied);
        var totalCrabs = crabs.Count;
        var openAlerts = alerts.Count;
        var online = devices.Count(d => d.Status == DeviceStatus.Online);
        var iotPct = devices.Count == 0 ? 0 : Math.Round(online * 100.0 / devices.Count, 1);
        // ============================================================
        // MỚI: TÍNH KPI SUMMARY
        // ============================================================
        var kpiSummary = await CalculateKpiSummaryAsync(scope, ct);

        return ApiResponse<DashboardOverviewDto>.Ok(new DashboardOverviewDto(
            totalBoxes,
            totalCrabs,
            activeBoxes,
            openAlerts,
            iotPct,
            DateTime.UtcNow,
            kpiSummary));
    }

    public async Task<ApiResponse<DashboardMetricsDto>> GetMetricsAsync(
        Guid? farmingAreaId = null, CancellationToken ct = default)
    {
        var scope = await ResolveScopeAsync(farmingAreaId, ct);
        var devices = scope.Devices;
        var boxes = scope.Boxes;
        var sensors = scope.Sensors;
        var activeAlerts = await FilterAlertsAsync(scope, ct);

        int? deviceScore = devices.Count == 0
            ? null
            : (int)Math.Round(devices.Count(d => d.Status == DeviceStatus.Online) * 100.0 / devices.Count);

        var occupied = boxes.Count(b => b.IsOccupied
            || !string.Equals(b.Status, BoxStatuses.Empty, StringComparison.OrdinalIgnoreCase));
        var healthyBoxes = boxes.Count(b =>
            string.Equals(b.Status, BoxStatuses.Active, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase));
        int? crabScore = occupied == 0
            ? null
            : (int)Math.Round(healthyBoxes * 100.0 / Math.Max(occupied, 1));

        var quarantine = boxes.Count(b =>
            string.Equals(b.Status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase));
        if (crabScore.HasValue)
            crabScore = Math.Clamp(crabScore.Value - quarantine * 5
                - activeAlerts.Count(a => a.Severity == AlertSeverity.Critical) * 8, 0, 100);

        var activeSensors = sensors.Count(s => s.IsActive);
        var staleCutoff = DateTime.UtcNow.AddHours(-2);
        var freshSensors = sensors.Count(s => s.IsActive && s.LastSeenAt != null && s.LastSeenAt > staleCutoff);
        int? waterScore = activeSensors == 0
            ? null
            : (int)Math.Round(freshSensors * 100.0 / activeSensors);
        if (waterScore.HasValue)
            waterScore = Math.Clamp(waterScore.Value
                - activeAlerts.Count(a => a.Severity >= AlertSeverity.Warning) * 4, 0, 100);

        var components = new[] { waterScore, crabScore, deviceScore }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        int? score = components.Length == 0
            ? null
            : Math.Clamp((int)Math.Round(components.Average()), 0, 100);

        var (level, label) = score switch
        {
            >= 85 => ("excellent", "Excellent"),
            >= 70 => ("good", "Good"),
            >= 50 => ("warning", "Warning"),
            not null => ("danger", "Critical"),
            _ => ("no_data", "No data")
        };

        var areaLabel = farmingAreaId.HasValue ? "khu đang chọn" : "trang trại";
        var explanation = score is null
            ? $"Chưa có dữ liệu thiết bị, cảm biến hoặc cua cho {areaLabel}."
            : activeAlerts.Count > 0
            ? $"Có {activeAlerts.Count} cảnh báo đang mở trên {areaLabel}; điểm thiết bị {deviceScore}%, nước {waterScore}%, cua {crabScore}%."
            : $"Điểm theo dõi vận hành {areaLabel}: độ phủ sensor mới {waterScore}%, cua {crabScore}%, thiết bị {deviceScore}%. Không thay thế kết quả đo chất lượng nước.";

        return ApiResponse<DashboardMetricsDto>.Ok(new DashboardMetricsDto(
            score,
            level,
            label,
            DeltaVsYesterday: null,
            DateTime.UtcNow,
            waterScore,
            crabScore,
            deviceScore,
            explanation));
    }

    public async Task<ApiResponse<List<AiRecommendationDto>>> GetRecommendationsAsync(
        Guid? farmingAreaId = null, CancellationToken ct = default)
    {
        var list = new List<AiRecommendationDto>();
        var scope = await ResolveScopeAsync(farmingAreaId, ct);
        var boxes = scope.Boxes;
        var alerts = await FilterAlertsAsync(scope, ct);

        foreach (var box in boxes.Where(b =>
                     string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)).Take(5))
        {
            list.Add(new AiRecommendationDto(
                Id: box.Id.ToString(),
                Type: "harvest",
                Title: $"Thu hoạch box {box.Code}",
                Description: $"Box {box.Code} đang ở trạng thái lột — nên kiểm tra cửa sổ softshell và thu hoạch khi đạt.",
                TargetBoxOrArea: box.Code,
                ConfidencePercentage: null,
                Priority: "high",
                Reason: "Box status = molting",
                OptimalTimeframe: "Trong 6 giờ",
                ExpectedImpact: "Tăng tỷ lệ cua lột loại A",
                HasActiveRecommendation: true));
        }

        foreach (var alert in alerts.Where(a => a.Severity >= AlertSeverity.Warning).Take(3))
        {
            list.Add(new AiRecommendationDto(
                Id: alert.Id.ToString(),
                Type: alert.Severity == AlertSeverity.Critical ? "waterTreatment" : "inspect",
                Title: alert.Severity == AlertSeverity.Critical ? "Xử lý cảnh báo nghiêm trọng" : "Kiểm tra cảnh báo",
                Description: string.IsNullOrWhiteSpace(alert.Message)
                    ? "Cần kiểm tra thông số môi trường / thiết bị."
                    : alert.Message,
                TargetBoxOrArea: farmingAreaId.HasValue ? "Khu đang chọn" : "Trang trại",
                ConfidencePercentage: null,
                Priority: alert.Severity == AlertSeverity.Critical ? "high" : "medium",
                Reason: $"Cảnh báo {alert.Severity}",
                OptimalTimeframe: "Ngay khi có thể",
                ExpectedImpact: "Ổn định chất lượng nước / thiết bị",
                HasActiveRecommendation: true));
        }

        if (list.Count == 0)
        {
            list.Add(new AiRecommendationDto(
                Id: "none",
                Type: "observe",
                Title: "Chưa có khuyến nghị theo quy tắc",
                Description: "Không có đủ tín hiệu để sinh khuyến nghị; tiếp tục theo dõi dữ liệu vận hành.",
                TargetBoxOrArea: farmingAreaId.HasValue ? "Khu đang chọn" : "Toàn trang trại",
                ConfidencePercentage: null,
                Priority: "low",
                Reason: "NoDataOrNoRuleMatch",
                OptimalTimeframe: "Duy trì giám sát",
                ExpectedImpact: "Ổn định vận hành",
                HasActiveRecommendation: false));
        }

        return ApiResponse<List<AiRecommendationDto>>.Ok(list);
    }

    private async Task<List<Domain.Entities.Alert>> FilterAlertsAsync(AreaScope scope, CancellationToken ct)
    {
        var alerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();
        if (!scope.IsFiltered) return alerts;
        return alerts
            .Where(a => a.SensorId is Guid sid && scope.SensorIds.Contains(sid))
            .ToList();
    }

    private async Task<AreaScope> ResolveScopeAsync(Guid? farmingAreaId, CancellationToken ct)
    {
        var allowed = await _areas.SelectionAsync(farmingAreaId, ct);
        if (allowed is null)
        {
            var allBoxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
            var allSensors = (await _uow.Sensors.GetAllAsync(ct)).ToList();
            var allDevices = (await _uow.Devices.GetAllAsync(ct)).ToList();
            return AreaScope.Unfiltered(allBoxes, allSensors, allDevices);
        }

        var rowIds = (await _uow.FarmingRows.GetAllAsync(ct))
            .Where(row => allowed.Contains(row.FarmingAreaId))
            .Select(row => row.Id)
            .ToHashSet();
        var boxes = (await _uow.Boxes.GetAllAsync(ct))
            .Where(box => rowIds.Contains(box.FarmingRowId))
            .ToList();
        var boxIds = boxes.Select(box => box.Id).ToHashSet();

        var waterSystemIds = (await _uow.WaterSystems.GetAllAsync(ct))
            .Where(system => system.FarmingAreaId is Guid areaId && allowed.Contains(areaId))
            .Select(system => system.Id)
            .ToHashSet();
        var sensors = (await _uow.Sensors.GetAllAsync(ct))
            .Where(sensor => sensor.WaterSystemId is Guid systemId && waterSystemIds.Contains(systemId))
            .ToList();
        var sensorIds = sensors.Select(sensor => sensor.Id).ToHashSet();
        var deviceIds = sensors.Where(sensor => sensor.DeviceId is Guid).Select(sensor => sensor.DeviceId!.Value).ToHashSet();
        var devices = (await _uow.Devices.GetAllAsync(ct))
            .Where(device => deviceIds.Contains(device.Id)
                || (device.FarmingAreaId is Guid areaId && allowed.Contains(areaId)))
            .ToList();

        return new AreaScope(true, boxes, boxIds, sensors, sensorIds, devices, allowed);
    }

    private sealed class AreaScope
    {
        public bool IsFiltered { get; }
        public List<Domain.Entities.Box> Boxes { get; }
        public HashSet<Guid> BoxIds { get; }
        public List<Domain.Entities.Sensor> Sensors { get; }
        public HashSet<Guid> SensorIds { get; }
        public List<Domain.Entities.Device> Devices { get; }
        public HashSet<Guid>? AreaIds { get; }

        public AreaScope(
            bool isFiltered,
            List<Domain.Entities.Box> boxes,
            HashSet<Guid> boxIds,
            List<Domain.Entities.Sensor> sensors,
            HashSet<Guid> sensorIds,
            List<Domain.Entities.Device> devices,
            HashSet<Guid>? areaIds = null)
        {
            IsFiltered = isFiltered;
            Boxes = boxes;
            BoxIds = boxIds;
            Sensors = sensors;
            SensorIds = sensorIds;
            Devices = devices;
            AreaIds = areaIds;
        }

        public static AreaScope Unfiltered(
            List<Domain.Entities.Box> boxes,
            List<Domain.Entities.Sensor> sensors,
            List<Domain.Entities.Device> devices) =>
            new(false, boxes, boxes.Select(b => b.Id).ToHashSet(),
                sensors, sensors.Select(s => s.Id).ToHashSet(), devices);
    }
    // ============================================================
    // MỚI: TÍNH KPI SUMMARY CHO DASHBOARD
    // ============================================================
    private async Task<DashboardKpiSummaryDto?> CalculateKpiSummaryAsync(
        AreaScope scope, CancellationToken ct)
    {
        var crabs = (await _uow.Crabs.GetAllAsync(ct))
            .Where(c => c.BoxId is Guid bid && scope.BoxIds.Contains(bid))
            .ToList();

        if (crabs.Count == 0) return null;

        var total = crabs.Count;

        var alive = crabs.Count(c =>
            c.Status == CrabStatus.Alive
            || c.Status == CrabStatus.Molting
            || c.Status == CrabStatus.Quarantined);

        var dead = crabs.Count(c => c.Status == CrabStatus.Dead);
        var harvested = crabs.Count(c => c.Status == CrabStatus.Harvested);
        var molting = crabs.Count(c => c.Status == CrabStatus.Molting);

        var survivalRate = total > 0
            ? decimal.Round((decimal)alive / total * 100, 2) : 0;
        var moltingRate = total > 0
            ? decimal.Round((decimal)molting / total * 100, 2) : 0;
        var harvestRate = total > 0
            ? decimal.Round((decimal)harvested / total * 100, 2) : 0;
        var mortalityRate = total > 0
            ? decimal.Round((decimal)dead / total * 100, 2) : 0;

        // Tổng sản lượng thu hoạch (từ HarvestVouchers)
        var harvestVouchers = (await _uow.HarvestVouchers.FindAsync(
                v => v.Status == HarvestStatus.Completed, ct))
            .Where(voucher => scope.AreaIds is null
                || (voucher.FarmingAreaId is Guid areaId && scope.AreaIds.Contains(areaId)));
        var totalHarvestWeightKg = harvestVouchers.Sum(v => v.TotalWeightKg);

        // Tồn kho cấp đông
        HashSet<Guid>? areaVoucherIds = null;

        if (scope.AreaIds is not null)
        {
            var areaVouchers = (await _uow.HarvestVouchers.GetAllAsync(ct))
                .Where(voucher => voucher.FarmingAreaId is Guid areaId && scope.AreaIds.Contains(areaId));

            areaVoucherIds = areaVouchers
                .Select(v => v.Id)
                .ToHashSet();
        }
        var frozenLots = await _uow.FrozenLots.GetAllAsync(ct);
        var frozenWeightKg = frozenLots
            .Where(l =>
                (l.Status == FrozenLotStatus.Available ||
                l.Status == FrozenLotStatus.Reserved) &&
                (areaVoucherIds is null ||
                (l.HarvestVoucherId.HasValue &&
                areaVoucherIds.Contains(l.HarvestVoucherId.Value))))
            .Sum(l => l.WeightKg);
        return new DashboardKpiSummaryDto(
            SurvivalRate: survivalRate,
            MoltingRate: moltingRate,
            HarvestRate: harvestRate,
            MortalityRate: mortalityRate,
            TotalHarvestWeightKg: totalHarvestWeightKg,
            FrozenInventoryWeightKg: frozenWeightKg);
    }
}
