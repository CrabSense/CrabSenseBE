using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Dashboard;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;

    public DashboardService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<DashboardOverviewDto>> GetOverviewAsync(CancellationToken ct = default)
    {
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var crabs = (await _uow.Crabs.GetAllAsync(ct)).ToList();
        var alerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();
        var devices = (await _uow.Devices.GetAllAsync(ct)).ToList();

        var totalBoxes = boxes.Count;
        var activeBoxes = boxes.Count(b =>
            string.Equals(b.Status, BoxStatuses.Active, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)
            || b.IsOccupied);
        var totalCrabs = crabs.Count(c => c.Status == CrabStatus.Alive);
        var openAlerts = alerts.Count;
        var online = devices.Count(d => d.Status == DeviceStatus.Online);
        var iotPct = devices.Count == 0 ? 0 : Math.Round(online * 100.0 / devices.Count, 1);

        return ApiResponse<DashboardOverviewDto>.Ok(new DashboardOverviewDto(
            totalBoxes,
            totalCrabs,
            activeBoxes,
            openAlerts,
            iotPct,
            DateTime.UtcNow));
    }

    public async Task<ApiResponse<DashboardMetricsDto>> GetMetricsAsync(CancellationToken ct = default)
    {
        var devices = (await _uow.Devices.GetAllAsync(ct)).ToList();
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var sensors = (await _uow.Sensors.GetAllAsync(ct)).ToList();
        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();

        var deviceScore = devices.Count == 0
            ? 80
            : (int)Math.Round(devices.Count(d => d.Status == DeviceStatus.Online) * 100.0 / devices.Count);

        var occupied = boxes.Count(b => b.IsOccupied
            || !string.Equals(b.Status, BoxStatuses.Empty, StringComparison.OrdinalIgnoreCase));
        var healthyBoxes = boxes.Count(b =>
            string.Equals(b.Status, BoxStatuses.Active, StringComparison.OrdinalIgnoreCase)
            || string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase));
        var crabScore = occupied == 0
            ? 85
            : (int)Math.Round(healthyBoxes * 100.0 / Math.Max(occupied, 1));

        // Penalize for quarantine / critical alerts
        var quarantine = boxes.Count(b =>
            string.Equals(b.Status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase));
        crabScore = Math.Clamp(crabScore - quarantine * 5 - activeAlerts.Count(a => a.Severity == AlertSeverity.Critical) * 8, 0, 100);

        var activeSensors = sensors.Count(s => s.IsActive);
        var staleCutoff = DateTime.UtcNow.AddHours(-2);
        var freshSensors = sensors.Count(s => s.IsActive && s.LastSeenAt != null && s.LastSeenAt > staleCutoff);
        var waterScore = activeSensors == 0
            ? 82
            : (int)Math.Round(Math.Max(freshSensors, activeSensors * 0.7) * 100.0 / activeSensors);
        waterScore = Math.Clamp(waterScore - activeAlerts.Count(a => a.Severity >= AlertSeverity.Warning) * 4, 0, 100);

        var score = (int)Math.Round(waterScore * 0.35 + crabScore * 0.40 + deviceScore * 0.25);
        score = Math.Clamp(score, 0, 100);

        var (level, label) = score switch
        {
            >= 85 => ("excellent", "Excellent"),
            >= 70 => ("good", "Good"),
            >= 50 => ("warning", "Warning"),
            _ => ("danger", "Critical")
        };

        var explanation = activeAlerts.Count > 0
            ? $"Có {activeAlerts.Count} cảnh báo đang mở; điểm thiết bị {deviceScore}%, nước {waterScore}%, cua {crabScore}%."
            : $"Trang trại ổn định — nước {waterScore}%, cua {crabScore}%, thiết bị {deviceScore}%.";

        return ApiResponse<DashboardMetricsDto>.Ok(new DashboardMetricsDto(
            score,
            level,
            label,
            DeltaVsYesterday: 0,
            DateTime.UtcNow,
            waterScore,
            crabScore,
            deviceScore,
            explanation));
    }

    public async Task<ApiResponse<List<AiRecommendationDto>>> GetRecommendationsAsync(CancellationToken ct = default)
    {
        var list = new List<AiRecommendationDto>();
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var alerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).ToList();

        foreach (var box in boxes.Where(b =>
                     string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)).Take(5))
        {
            list.Add(new AiRecommendationDto(
                Id: box.Id.ToString(),
                Type: "harvest",
                Title: $"Thu hoạch box {box.Code}",
                Description: $"Box {box.Code} đang ở trạng thái lột — nên kiểm tra cửa sổ softshell và thu hoạch khi đạt.",
                TargetBoxOrArea: box.Code,
                ConfidencePercentage: 88,
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
                TargetBoxOrArea: "Trang trại",
                ConfidencePercentage: alert.Severity == AlertSeverity.Critical ? 95 : 80,
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
                Title: "Không có hành động khẩn cấp",
                Description: "Hệ thống đang hoạt động ổn định",
                TargetBoxOrArea: "Toàn trang trại",
                ConfidencePercentage: 99,
                Priority: "low",
                Reason: "Không có box lột hoặc cảnh báo ưu tiên",
                OptimalTimeframe: "Duy trì giám sát",
                ExpectedImpact: "Ổn định vận hành",
                HasActiveRecommendation: false));
        }

        return ApiResponse<List<AiRecommendationDto>>.Ok(list);
    }
}
