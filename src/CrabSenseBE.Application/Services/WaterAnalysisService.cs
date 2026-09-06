using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.IoT;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

/// <summary>Phân tích hóa học màu — tách biệt ingest cảm biến realtime.</summary>
public class WaterAnalysisService : IWaterAnalysisService
{
    public static readonly string[] StepLabels =
    [
        "Lấy mẫu nước",
        "Bơm thuốc thử",
        "Chờ phản ứng",
        "Camera chụp màu",
        "AI phân tích",
        "Lưu kết quả"
    ];

    private static readonly TimeSpan StepHold = TimeSpan.FromSeconds(2);

    private readonly IUnitOfWork _uow;

    public WaterAnalysisService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<WaterAnalysisSnapshotDto>> GetSnapshotAsync(
        Guid areaId, CancellationToken ct = default)
    {
        _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        await AdvanceActiveAsync(areaId, ct);
        return ApiResponse<WaterAnalysisSnapshotDto>.Ok(await BuildAsync(areaId, ct));
    }

    public async Task<ApiResponse<WaterAnalysisSnapshotDto>> StartAsync(
        Guid areaId, CancellationToken ct = default)
    {
        _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        var active = (await _uow.WaterAnalysisRuns.FindAsync(
                r => r.FarmingAreaId == areaId && r.Status == "running", ct))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();
        if (active is null)
        {
            var now = DateTime.UtcNow;
            active = new WaterAnalysisRun
            {
                FarmingAreaId = areaId,
                Status = "running",
                CurrentStep = 1,
                StartedAt = now,
                LastStepAt = now,
                Source = "colorimetric-ai"
            };
            await _uow.WaterAnalysisRuns.AddAsync(active, ct);
            await _uow.SaveChangesAsync(ct);
        }

        await AdvanceActiveAsync(areaId, ct);
        return ApiResponse<WaterAnalysisSnapshotDto>.Ok(
            await BuildAsync(areaId, ct),
            "Đã bắt đầu phân tích nước.");
    }

    private async Task AdvanceActiveAsync(Guid areaId, CancellationToken ct)
    {
        var run = (await _uow.WaterAnalysisRuns.FindAsync(
                r => r.FarmingAreaId == areaId && r.Status == "running", ct))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();
        if (run is null) return;

        var now = DateTime.UtcNow;
        var elapsed = now - run.LastStepAt;
        var hops = (int)Math.Floor(elapsed.TotalSeconds / StepHold.TotalSeconds);
        if (hops <= 0) return;

        run.CurrentStep = Math.Min(6, run.CurrentStep + hops);
        run.LastStepAt = now;
        if (run.CurrentStep >= 6)
        {
            Complete(run, now);
        }
        _uow.WaterAnalysisRuns.Update(run);
        await _uow.SaveChangesAsync(ct);
    }

    private static void Complete(WaterAnalysisRun run, DateTime now)
    {
        run.Status = "completed";
        run.CurrentStep = 6;
        run.CompletedAt = now;
        // Stub màu học — thay bằng AI camera khi có phần cứng. Không đọc sensor realtime.
        var seed = run.Id.GetHashCode();
        var rng = new Random(seed);
        run.Ph = Math.Round(7.6m + (decimal)(rng.NextDouble() * 0.5), 2);
        run.Nh3 = Math.Round(0.015m + (decimal)(rng.NextDouble() * 0.04), 3);
        run.No2 = Math.Round(0.06m + (decimal)(rng.NextDouble() * 0.12), 3);
        run.No3 = Math.Round(12m + (decimal)(rng.NextDouble() * 16), 1);
        run.Source = "colorimetric-ai";
    }

    private async Task<WaterAnalysisSnapshotDto> BuildAsync(Guid areaId, CancellationToken ct)
    {
        var runs = (await _uow.WaterAnalysisRuns.FindAsync(r => r.FarmingAreaId == areaId, ct))
            .OrderByDescending(r => r.StartedAt)
            .ToList();
        var latest = runs.FirstOrDefault(r => r.Status == "completed");
        var active = runs.FirstOrDefault(r => r.Status == "running");
        return new WaterAnalysisSnapshotDto(
            latest is null ? null : MapRun(latest),
            active is null ? null : MapRun(active),
            await StationAsync(areaId, ct));
    }

    private async Task<IReadOnlyList<WaterAnalysisStationPartDto>> StationAsync(
        Guid areaId, CancellationToken ct)
    {
        var devices = (await _uow.Devices.FindAsync(
                d => d.FarmingAreaId == areaId, ct))
            .ToList();
        if (devices.Count == 0)
        {
            var wsIds = (await _uow.WaterSystems.FindAsync(w => w.FarmingAreaId == areaId, ct))
                .Select(w => w.Id)
                .ToHashSet();
            var deviceIds = (await _uow.Sensors.GetAllAsync(ct))
                .Where(s => s.DeviceId != null && s.WaterSystemId != null && wsIds.Contains(s.WaterSystemId.Value))
                .Select(s => s.DeviceId!.Value)
                .ToHashSet();
            devices = (await _uow.Devices.GetAllAsync(ct))
                .Where(d => deviceIds.Contains(d.Id) || d.FarmingAreaId == areaId)
                .ToList();
        }

        bool Online(string kind) => devices.Any(d =>
            (d.DeviceType ?? "").Contains(kind, StringComparison.OrdinalIgnoreCase)
            && d.Status == DeviceStatus.Online);

        var esp = devices.Any(d =>
            (d.DeviceType ?? "esp32").Contains("esp32", StringComparison.OrdinalIgnoreCase)
            && d.Status == DeviceStatus.Online);
        var cam = Online("camera") || devices.Any(d =>
            (d.DeviceCode ?? "").StartsWith("CAM", StringComparison.OrdinalIgnoreCase)
            && d.Status == DeviceStatus.Online);

        return
        [
            new("reagent", "Thuốc thử", true, "Sẵn sàng"),
            new("samplePump", "Bơm nước mẫu", esp, esp ? "Sẵn sàng" : "Chưa kết nối ESP"),
            new("camera", "Camera", cam, cam ? "Online" : "Offline"),
            new("ai", "AI Analysis", true, "Ready")
        ];
    }

    private static WaterAnalysisRunDto MapRun(WaterAnalysisRun r) =>
        new(r.Id, r.Status, r.CurrentStep, StepLabel(r.CurrentStep),
            r.StartedAt, r.CompletedAt, MetricsOf(r), r.Error);

    public static string StepLabel(int step)
    {
        var i = Math.Clamp(step, 1, 6) - 1;
        return StepLabels[i];
    }

    private static IReadOnlyList<WaterAnalysisMetricDto> MetricsOf(WaterAnalysisRun r) =>
    [
        Metric("ph", "pH", r.Ph, "", EvaluatePh),
        Metric("nh3", "NH3", r.Nh3, "mg/L", EvaluateNh3),
        Metric("no2", "NO2", r.No2, "mg/L", EvaluateNo2),
        Metric("no3", "NO3", r.No3, "mg/L", EvaluateNo3)
    ];

    private static WaterAnalysisMetricDto Metric(
        string code, string label, decimal? value, string unit,
        Func<decimal, (string Status, string Label)> eval)
    {
        if (value is null)
            return new(code, label, null, unit, "pending", "Chưa có");
        var (st, lb) = eval(value.Value);
        return new(code, label, value, unit, st, lb);
    }

    private static (string, string) EvaluatePh(decimal v)
    {
        if (v >= 7.5m && v <= 8.5m) return ("good", "Tốt");
        if (v >= 7.0m && v <= 8.8m) return ("watch", "Theo dõi");
        return ("alert", "Vượt ngưỡng");
    }

    private static (string, string) EvaluateNh3(decimal v)
    {
        if (v <= 0.05m) return ("good", "An toàn");
        if (v <= 0.1m) return ("watch", "Theo dõi");
        return ("alert", "Nguy hiểm");
    }

    private static (string, string) EvaluateNo2(decimal v)
    {
        if (v < 0.08m) return ("good", "Bình thường");
        if (v <= 0.2m) return ("watch", "Theo dõi");
        return ("alert", "Vượt ngưỡng");
    }

    private static (string, string) EvaluateNo3(decimal v)
    {
        if (v <= 40m) return ("good", "Bình thường");
        if (v <= 80m) return ("watch", "Theo dõi");
        return ("alert", "Cao");
    }
}
