using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Dashboard;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class OperationLogService : IOperationLogService
{
    private readonly IUnitOfWork _uow;

    public OperationLogService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<List<OperationTaskDto>>> GetTodayTasksAsync(CancellationToken ct = default)
    {
        var tasks = new List<OperationTaskDto>();
        var boxes = (await _uow.Boxes.GetAllAsync(ct)).ToList();
        var endOfDay = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);

        foreach (var box in boxes.Where(b =>
                     string.Equals(b.Status, BoxStatuses.Molting, StringComparison.OrdinalIgnoreCase)))
        {
            tasks.Add(new OperationTaskDto(
                Id: $"molt-{box.Id}",
                Title: "Kiểm tra / thu hoạch cua lột",
                Target: box.Code,
                Deadline: endOfDay,
                Priority: "high",
                IsCompleted: false));
        }

        foreach (var box in boxes.Where(b =>
                     string.Equals(b.Status, BoxStatuses.Quarantine, StringComparison.OrdinalIgnoreCase)))
        {
            tasks.Add(new OperationTaskDto(
                Id: $"quar-{box.Id}",
                Title: "Theo dõi box cách ly",
                Target: box.Code,
                Deadline: endOfDay,
                Priority: "medium",
                IsCompleted: false));
        }

        var activeAlerts = (await _uow.Alerts.FindAsync(a => a.Status == AlertStatus.Active, ct)).Take(5);
        foreach (var alert in activeAlerts)
        {
            tasks.Add(new OperationTaskDto(
                Id: $"alert-{alert.Id}",
                Title: "Xử lý cảnh báo",
                Target: alert.Message.Length > 40 ? alert.Message[..40] + "…" : alert.Message,
                Deadline: endOfDay,
                Priority: alert.Severity == AlertSeverity.Critical ? "high" : "medium",
                IsCompleted: false));
        }

        if (tasks.Count == 0)
        {
            tasks.Add(new OperationTaskDto(
                Id: "routine-feed",
                Title: "Kiểm tra cho ăn định kỳ",
                Target: "Toàn trang trại",
                Deadline: endOfDay,
                Priority: "low",
                IsCompleted: false));
        }

        return ApiResponse<List<OperationTaskDto>>.Ok(tasks);
    }

    public async Task<ApiResponse<List<RecentActivityDto>>> GetRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var logs = (await _uow.OperationLogs.GetAllAsync(ct))
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .ToList();

        var items = logs.Select(l =>
        {
            var type = MapActivityType(l.Action, l.EntityType);
            return new RecentActivityDto(
                Id: l.Id.ToString(),
                Title: HumanizeAction(l.Action),
                Description: l.Details ?? $"{l.EntityType ?? "system"} {l.EntityId}",
                Type: type,
                Timestamp: l.CreatedAt);
        }).ToList();

        // Fallback: surface recent alerts as activity if no logs
        if (items.Count == 0)
        {
            var alerts = (await _uow.Alerts.GetAllAsync(ct))
                .OrderByDescending(a => a.CreatedAt)
                .Take(limit)
                .ToList();
            items = alerts.Select(a => new RecentActivityDto(
                Id: a.Id.ToString(),
                Title: "Cảnh báo hệ thống",
                Description: a.Message,
                Type: "alertHandled",
                Timestamp: a.CreatedAt)).ToList();
        }

        return ApiResponse<List<RecentActivityDto>>.Ok(items);
    }

    private static string MapActivityType(string action, string? entityType)
    {
        var a = (action ?? "").ToLowerInvariant();
        var e = (entityType ?? "").ToLowerInvariant();
        if (a.Contains("qr") || e.Contains("qr")) return "qrScan";
        if (a.Contains("sensor") || a.Contains("iot") || e.Contains("sensor")) return "sensorUpdate";
        if (a.Contains("ai") || a.Contains("detect")) return "aiDetection";
        if (a.Contains("harvest") || e.Contains("harvest")) return "harvest";
        if (a.Contains("sync")) return "sync";
        if (a.Contains("alert")) return "alertHandled";
        return "sync";
    }

    private static string HumanizeAction(string action) =>
        string.IsNullOrWhiteSpace(action)
            ? "Hoạt động hệ thống"
            : action.Replace('_', ' ');
}
