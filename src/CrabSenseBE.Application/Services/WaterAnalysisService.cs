using System.Text.Json;
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
    public const int TotalSteps = 8;

    public static readonly string[] StepLabels =
    [
        "Lấy mẫu nước",
        "Thêm thuốc thử #1",
        "Chờ phản ứng",
        "Thêm thuốc thử #2",
        "Camera chụp mẫu",
        "AI phân tích",
        "Lưu kết quả",
        "Xả & làm sạch"
    ];

    public static readonly string[] StepCommands =
    [
        "RUN_SAMPLE_PUMP",
        "RUN_REAGENT_PUMP_1",
        "WAIT_REACTION",
        "RUN_REAGENT_PUMP_2",
        "CAPTURE_SAMPLE",
        "AI_ANALYZE",
        "SAVE_RESULT",
        "DRAIN_AND_CLEAN"
    ];

    public static readonly string[] StepEvents =
    [
        "SAMPLE_PUMP_COMPLETED",
        "REAGENT_1_COMPLETED",
        "REACTION_COMPLETED",
        "REAGENT_2_COMPLETED",
        "IMAGE_CAPTURED",
        "AI_COMPLETED",
        "ANALYSIS_RESULT_SAVED",
        "CLEANING_COMPLETED"
    ];

    private static readonly TimeSpan DefaultStepHold = TimeSpan.FromSeconds(3);

    private static readonly IReadOnlyList<WaterAnalysisSourceOptionDto> SourceOptions =
    [
        new("RAS_RETURN", "Nước tuần hoàn RAS"),
        new("RETURN_TANK", "Bể hồi"),
        new("BIO_TANK", "Bể vi sinh"),
        new("SETTLING_TANK", "Bể lắng"),
        new("INLET", "Nguồn nước cấp")
    ];

    private static readonly IReadOnlyList<WaterAnalysisThresholdDto> Thresholds =
    [
        new("ph", "pH", "", 7.5m, 8.5m, 7.0m, 8.8m, "7.5 – 8.5"),
        new("nh3", "NH3", "mg/L", null, 0.05m, null, 0.10m, "≤ 0.05 mg/L"),
        new("no2", "NO2", "mg/L", null, 0.05m, null, 0.20m, "≤ 0.05 mg/L"),
        new("no3", "NO3", "mg/L", null, 20m, null, 40m, "< 20 mg/L")
    ];

    private static readonly IReadOnlyList<WaterAnalysisAssayDto> Assays =
    [
        new("NO2", "NO2", true, 5m, 15, "theo cấu hình assay", "theo cấu hình assay", "NO2-V1", 0.75m),
        new("NH3", "NH3", false, null, null, null, null, null, 0.75m),
        new("NO3", "NO3", false, null, null, null, null, null, 0.75m),
        new("PH", "pH", false, null, null, null, null, null, 0.75m)
    ];

    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _currentUser;

    public WaterAnalysisService(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        _uow = uow;
        _currentUser = currentUser;
    }

    public async Task<ApiResponse<WaterAnalysisSnapshotDto>> GetSnapshotAsync(
        Guid areaId, CancellationToken ct = default)
    {
        _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        await AdvanceActiveAsync(areaId, ct);
        return ApiResponse<WaterAnalysisSnapshotDto>.Ok(await BuildAsync(areaId, ct));
    }

    public async Task<ApiResponse<WaterAnalysisSnapshotDto>> StartAsync(
        Guid areaId, StartWaterAnalysisRequest? req, CancellationToken ct = default)
    {
        _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        var analyte = NormalizeAnalyte(req?.Analyte);
        var assay = Assays.FirstOrDefault(a => a.Analyte == analyte);
        if (assay is null || !assay.Configured)
            throw AppException.BadRequest($"Chỉ tiêu {analyte} chưa được cấu hình assay.");

        var snapshot = await BuildAsync(areaId, ct);
        if (snapshot.Active is not null)
            throw AppException.BadRequest(
                $"Hệ thống đang bận. {snapshot.Active.TestCode ?? snapshot.Active.Id.ToString()} đang chạy {snapshot.Active.AnalyteLabel ?? "phân tích"}.");

        if (!snapshot.CanStart)
        {
            var detail = snapshot.Blockers is { Count: > 0 }
                ? string.Join(" ", snapshot.Blockers.Select(b => $"{b.Label}: {b.Reason}."))
                : "Thiết bị bắt buộc chưa sẵn sàng.";
            throw AppException.BadRequest($"Chưa thể bắt đầu phân tích. {detail}");
        }

        var now = DateTime.UtcNow;
        var source = NormalizeSource(req?.SampleSource) ?? snapshot.Sample?.SampleSource ?? "RAS_RETURN";
        var hardware = await ResolveHardwareAsync(areaId, ct);
        var actor = await ResolveUserNameAsync(ct);
        var testCode = await NextTestCodeAsync(areaId, now, ct);

        var run = new WaterAnalysisRun
        {
            FarmingAreaId = areaId,
            Status = "running",
            CurrentStep = 1,
            StartedAt = now,
            LastStepAt = now,
            Source = "colorimetric-ai",
            Analyte = analyte,
            SampleSource = source,
            SampleLocation = string.IsNullOrWhiteSpace(req?.SampleLocation)
                ? snapshot.Sample?.SampleLocation
                : req!.SampleLocation!.Trim(),
            Notes = string.IsNullOrWhiteSpace(req?.Notes) ? snapshot.Sample?.Notes : req!.Notes!.Trim(),
            TestCode = testCode,
            PerformedBy = actor,
            ControllerId = hardware.ControllerCode,
            CameraId = hardware.CameraCode,
            HardwareJson = HardwareSnapshotJson(analyte, hardware),
            StepLogJson = WriteLogs(
            [
                new WaterAnalysisStepLogDto(now, 0, "ANALYSIS_STARTED", $"Bắt đầu phân tích {analyte}")
            ])
        };
        await _uow.WaterAnalysisRuns.AddAsync(run, ct);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<WaterAnalysisSnapshotDto>.Ok(
            await BuildAsync(areaId, ct),
            $"Đã bắt đầu phân tích {assay.Label}.");
    }

    public async Task<ApiResponse<WaterAnalysisSnapshotDto>> StopAsync(
        Guid areaId, CancellationToken ct = default)
    {
        _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        var run = await ActiveRunAsync(areaId, ct)
            ?? throw AppException.BadRequest("Không có lần phân tích đang chạy.");

        var now = DateTime.UtcNow;
        var logs = ReadLogs(run.StepLogJson);
        logs.Add(new WaterAnalysisStepLogDto(now, run.CurrentStep, "ANALYSIS_CANCELLED",
            "Người vận hành dừng quy trình — chuyển sang xả/làm sạch an toàn."));
        logs.Add(new WaterAnalysisStepLogDto(now, TotalSteps, "CLEANING_COMPLETED",
            "Xả & làm sạch (dừng an toàn)."));
        run.Status = "cancelled";
        run.CurrentStep = TotalSteps;
        run.CompletedAt = now;
        run.LastStepAt = now;
        run.StepLogJson = WriteLogs(logs);
        run.Error = "Đã dừng. Hệ thống đã chuyển sang bước xả/làm sạch an toàn.";
        _uow.WaterAnalysisRuns.Update(run);
        await _uow.SaveChangesAsync(ct);

        return ApiResponse<WaterAnalysisSnapshotDto>.Ok(
            await BuildAsync(areaId, ct),
            "Đã dừng phân tích. Hệ thống đã xả/làm sạch.");
    }

    public async Task<ApiResponse<WaterAnalysisSnapshotDto>> UpdateSampleAsync(
        Guid areaId, UpdateWaterAnalysisSampleRequest req, CancellationToken ct = default)
    {
        _ = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");

        var source = NormalizeSource(req.SampleSource);
        if (source is null)
            throw AppException.BadRequest("Nguồn mẫu không hợp lệ.");

        var latest = (await _uow.WaterAnalysisRuns.FindAsync(r => r.FarmingAreaId == areaId, ct))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();
        if (latest is not null)
        {
            latest.SampleSource = source;
            latest.SampleLocation = string.IsNullOrWhiteSpace(req.SampleLocation)
                ? null
                : req.SampleLocation.Trim();
            latest.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
            _uow.WaterAnalysisRuns.Update(latest);
            await _uow.SaveChangesAsync(ct);
        }

        return ApiResponse<WaterAnalysisSnapshotDto>.Ok(
            await BuildAsync(areaId, ct),
            "Đã cập nhật thông tin mẫu.");
    }

    public async Task<ApiResponse<WaterAnalysisDetailDto>> GetDetailAsync(
        Guid areaId, Guid runId, CancellationToken ct = default)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        var run = await _uow.WaterAnalysisRuns.GetByIdAsync(runId, ct)
            ?? throw AppException.NotFound("WaterAnalysisRun");
        if (run.FarmingAreaId != areaId)
            throw AppException.NotFound("WaterAnalysisRun");

        await AdvanceActiveAsync(areaId, ct);
        run = await _uow.WaterAnalysisRuns.GetByIdAsync(runId, ct)
            ?? throw AppException.NotFound("WaterAnalysisRun");

        var hardware = await ResolveHardwareAsync(areaId, ct);
        var station = StationOf(hardware);
        var snap = ReadHardware(run.HardwareJson);
        var analyte = NormalizeAnalyte(run.Analyte);
        var assay = Assays.FirstOrDefault(a => a.Analyte == analyte) ?? Assays[0];
        var code = analyte.ToLowerInvariant() switch
        {
            "ph" => "ph",
            "nh3" => "nh3",
            "no3" => "no3",
            _ => "no2"
        };
        var value = ValueOf(run, code);
        var thr = Thresholds.FirstOrDefault(t => t.Code == code);
        string evaluation;
        string evaluationLabel;
        if (value is null)
        {
            evaluation = run.Status == "failed" ? "FAILED" : "pending";
            evaluationLabel = run.Status == "failed" ? "Không có kết quả" : "Chưa có";
        }
        else
        {
            (evaluation, evaluationLabel) = Evaluate(code, value.Value);
        }

        decimal? over = null;
        if (value is not null && thr?.Max is not null && value > thr.Max)
            over = Math.Round(value.Value - thr.Max.Value, 3);

        var usedAi = run.Confidence is not null;
        var (confLevel, confLabel) = ConfidenceBand(run.Confidence, assay.AiConfidenceMin);

        var logs = ReadLogs(run.StepLogJson);
        var legacy = logs.Count == 0
                     || logs.All(l => l.Step == 0);
        var steps = BuildProcessSteps(run, logs, assay, value);
        var failedStep = steps.FirstOrDefault(s => s.Status == "FAILED");

        var runs = (await _uow.WaterAnalysisRuns.FindAsync(r => r.FarmingAreaId == areaId, ct))
            .Where(r => r.Id != run.Id && r.Status == "completed")
            .OrderByDescending(r => r.CompletedAt ?? r.StartedAt)
            .ToList();
        WaterAnalysisPreviousDto? prev = null;
        var prevRun = runs.FirstOrDefault(r =>
            NormalizeAnalyte(r.Analyte) == analyte && ValueOf(r, code) is not null);
        if (prevRun is not null)
        {
            var pv = ValueOf(prevRun, code);
            prev = new(
                prevRun.TestCode,
                pv,
                thr?.Unit ?? "mg/L",
                prevRun.CompletedAt ?? prevRun.StartedAt,
                value is not null && pv is not null ? Math.Round(value.Value - pv.Value, 3) : null);
        }

        var blockers = BlockersOf(station, await ActiveRunAsync(areaId, ct));
        var canRerun = assay.Configured && blockers.Count == 0;

        return ApiResponse<WaterAnalysisDetailDto>.Ok(new WaterAnalysisDetailDto(
            run.Id,
            run.TestCode,
            run.Status,
            RunStatusLabel(run.Status),
            analyte,
            AnalyteLabel(run.Analyte),
            value,
            string.IsNullOrWhiteSpace(thr?.Unit) && code == "ph" ? "" : (thr?.Unit ?? "mg/L"),
            evaluation,
            evaluationLabel,
            thr?.Display,
            over,
            run.Confidence,
            usedAi ? confLevel : "none",
            usedAi ? confLabel : null,
            usedAi,
            run.ImageUrl,
            area.Name,
            string.IsNullOrWhiteSpace(area.Code) ? area.Name : area.Code,
            run.SampleSource,
            SourceLabel(run.SampleSource),
            run.SampleLocation,
            run.Notes,
            "Thuốc thử + Camera + AI",
            run.PerformedBy,
            run.StartedAt,
            run.CompletedAt,
            HardwareItems(snap, hardware, station, assay),
            ColorReferenceOf(code, value),
            $"So sánh màu mẫu với màu tham chiếu để AI xác định nồng độ {AnalyteLabel(run.Analyte)}.",
            steps,
            legacy,
            run.Id.ToString(),
            snap.AiModelId ?? assay.AiModelId,
            null,
            hardware.FirmwareVersion,
            run.ImageUrl,
            prev,
            null,
            run.Error,
            failedStep?.Label,
            canRerun));
    }

    private async Task AdvanceActiveAsync(Guid areaId, CancellationToken ct)
    {
        var run = await ActiveRunAsync(areaId, ct);
        if (run is null) return;

        var hardware = await ResolveHardwareAsync(areaId, ct);
        var now = DateTime.UtcNow;
        var logs = ReadLogs(run.StepLogJson);
        var changed = false;

        while (run.Status == "running")
        {
            var hold = HoldForStep(run.CurrentStep, run.Analyte);
            if (now - run.LastStepAt < hold) break;

            if (run.CurrentStep == 5 && !hardware.CameraOnline)
            {
                Fail(run, now, logs, 5, "Không thể chụp ảnh mẫu. Camera mất kết nối.",
                    "IMAGE_CAPTURE_FAILED");
                changed = true;
                break;
            }

            if (run.CurrentStep == 6 && !hardware.AiReady)
            {
                Fail(run, now, logs, 6, "AI không khả dụng — không ghi số giả.",
                    "AI_FAILED");
                changed = true;
                break;
            }

            logs.Add(new WaterAnalysisStepLogDto(now, run.CurrentStep,
                StepEvents[Math.Clamp(run.CurrentStep, 1, TotalSteps) - 1],
                DoneLabel(run.CurrentStep)));

            if (run.CurrentStep >= TotalSteps)
            {
                run.Status = "completed";
                run.CompletedAt = now;
                run.LastStepAt = now;
                run.StepLogJson = WriteLogs(logs);
                changed = true;
                break;
            }

            run.CurrentStep += 1;
            run.LastStepAt = now;
            run.StepLogJson = WriteLogs(logs);
            changed = true;
        }

        if (!changed) return;
        _uow.WaterAnalysisRuns.Update(run);
        await _uow.SaveChangesAsync(ct);
    }

    private static void Fail(
        WaterAnalysisRun run,
        DateTime now,
        List<WaterAnalysisStepLogDto> logs,
        int step,
        string reason,
        string evt)
    {
        logs.Add(new WaterAnalysisStepLogDto(now, step, evt, reason));
        logs.Add(new WaterAnalysisStepLogDto(now, TotalSteps, "ANALYSIS_FAILED",
            $"Thất bại ở bước {step}. Hệ thống chuyển sang xả/làm sạch."));
        run.Status = "failed";
        run.CurrentStep = TotalSteps;
        run.CompletedAt = now;
        run.LastStepAt = now;
        run.Error = reason;
        run.StepLogJson = WriteLogs(logs);
    }

    private async Task<WaterAnalysisSnapshotDto> BuildAsync(Guid areaId, CancellationToken ct)
    {
        var area = await _uow.FarmingAreas.GetByIdAsync(areaId, ct)
            ?? throw AppException.NotFound("FarmingArea");
        var runs = (await _uow.WaterAnalysisRuns.FindAsync(r => r.FarmingAreaId == areaId, ct))
            .OrderByDescending(r => r.StartedAt)
            .ToList();
        var latestCompleted = runs.FirstOrDefault(r => r.Status == "completed");
        var active = runs.FirstOrDefault(r => r.Status == "running");
        var hardware = await ResolveHardwareAsync(areaId, ct);
        var station = StationOf(hardware);
        var sample = SampleOf(area, runs.FirstOrDefault());
        var blockers = BlockersOf(station, active);
        var latest = latestCompleted is null
            ? null
            : MapRun(latestCompleted, hardware) with { Metrics = LatestMetrics(runs) };

        return new WaterAnalysisSnapshotDto(
            latest,
            active is null ? null : MapRun(active, hardware),
            station,
            runs.Take(20).Select(r => MapRun(r, hardware)).ToList(),
            Thresholds,
            sample,
            Assays,
            SourceOptions,
            blockers,
            blockers.Count == 0 && active is null,
            TrendOf(runs, "no2"),
            "no2",
            StepsOf(),
            hardware.SystemError);
    }

    private IReadOnlyList<WaterAnalysisMetricDto> LatestMetrics(IReadOnlyList<WaterAnalysisRun> runs)
    {
        decimal? Last(Func<WaterAnalysisRun, decimal?> pick) =>
            runs.Select(pick).FirstOrDefault(v => v is not null);
        return
        [
            Metric("ph", "pH", Last(r => r.Ph), ""),
            Metric("nh3", "NH3", Last(r => r.Nh3), "mg/L"),
            Metric("no2", "NO2", Last(r => r.No2), "mg/L"),
            Metric("no3", "NO3", Last(r => r.No3), "mg/L")
        ];
    }

    private static IReadOnlyList<WaterAnalysisStationPartDto> StationOf(HardwareState hw)
    {
        WaterAnalysisStationPartDto Part(
            string code, string label, bool ready, string readyState, string readyLabel,
            string offlineState, string offlineLabel, string? detail = null, string? level = null)
            => new(code, label, ready,
                ready ? readyState : offlineState,
                ready ? readyLabel : offlineLabel,
                detail, level);

        var pumpReady = hw.ControllerOnline;
        return
        [
            Part("controller", "Controller ESP32", hw.ControllerOnline,
                "ONLINE", "Online", "OFFLINE", "Mất kết nối", hw.ControllerCode),
            Part("samplePump", "Bơm nước mẫu", pumpReady,
                "READY", "Sẵn sàng", "NOT_CONNECTED", "Chưa kết nối ESP"),
            Part("reagentPump1", "Bơm thuốc thử NO2 #1", pumpReady,
                "READY", "Sẵn sàng", "NOT_CONNECTED", "Chưa kết nối ESP",
                level: "Chưa xác nhận"),
            Part("reagentPump2", "Bơm thuốc thử NO2 #2", pumpReady,
                "READY", "Sẵn sàng", "NOT_CONNECTED", "Chưa kết nối ESP",
                level: "Chưa xác nhận"),
            Part("drainValve", "Van xả", pumpReady,
                "READY", "Sẵn sàng", "NOT_CONNECTED", "Chưa kết nối ESP"),
            Part("camera", "Camera phân tích", hw.CameraOnline,
                "ONLINE", "Online", "OFFLINE", "Offline", hw.CameraCode),
            Part("ai", "AI Analysis", hw.AiReady,
                "READY", "Ready", "OFFLINE", "Không khả dụng")
        ];
    }

    private static List<WaterAnalysisBlockerDto> BlockersOf(
        IReadOnlyList<WaterAnalysisStationPartDto> station, WaterAnalysisRun? active)
    {
        var list = new List<WaterAnalysisBlockerDto>();
        if (active is not null)
        {
            list.Add(new("busy", "Hệ thống đang bận",
                $"{active.TestCode ?? active.Id.ToString()} đang chạy {active.Analyte ?? "phân tích"}."));
        }

        foreach (var p in station)
        {
            if (p.Ready) continue;
            list.Add(new(p.Code, p.Label, p.StateLabel));
        }

        return list;
    }

    private static WaterAnalysisSampleDto SampleOf(FarmingArea area, WaterAnalysisRun? hint)
    {
        var source = NormalizeSource(hint?.SampleSource) ?? "RAS_RETURN";
        return new(
            area.Name,
            string.IsNullOrWhiteSpace(area.Code) ? area.Name : area.Code,
            source,
            SourceLabel(source),
            hint?.SampleLocation,
            hint?.Notes);
    }

    private static IReadOnlyList<WaterAnalysisTrendPointDto> TrendOf(
        IReadOnlyList<WaterAnalysisRun> runs, string analyte)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-6);
        var points = new List<WaterAnalysisTrendPointDto>();
        for (var i = 0; i < 7; i++)
        {
            var day = cutoff.AddDays(i);
            var match = runs
                .Where(r => r.Status == "completed" && r.CompletedAt is not null
                            && r.CompletedAt.Value.ToLocalTime().Date == day.ToLocalTime().Date)
                .Select(r => (Run: r, Value: ValueOf(r, analyte)))
                .Where(x => x.Value is not null)
                .OrderByDescending(x => x.Run.CompletedAt)
                .FirstOrDefault();
            if (match.Run is null || match.Value is null) continue;
            var (st, lb) = Evaluate(analyte, match.Value.Value);
            points.Add(new(day, match.Value.Value, st, lb, match.Run.TestCode, match.Run.Id));
        }

        return points;
    }

    private static decimal? ValueOf(WaterAnalysisRun r, string analyte) => analyte switch
    {
        "ph" => r.Ph,
        "nh3" => r.Nh3,
        "no2" => r.No2,
        "no3" => r.No3,
        _ => null
    };

    private static IReadOnlyList<WaterAnalysisStepDefDto> StepsOf() =>
        Enumerable.Range(0, TotalSteps)
            .Select(i => new WaterAnalysisStepDefDto(i + 1, StepLabels[i], StepCommands[i]))
            .ToList();

    private WaterAnalysisRunDto MapRun(WaterAnalysisRun r, HardwareState hw)
    {
        var remaining = RemainingSeconds(r);
        return new(
            r.Id,
            r.Status,
            r.CurrentStep,
            StepLabel(r.CurrentStep),
            r.StartedAt,
            r.CompletedAt,
            MetricsOf(r),
            r.Error,
            r.TestCode,
            SessionStateOf(r),
            TotalSteps,
            ProgressOf(r),
            remaining,
            r.Analyte,
            AnalyteLabel(r.Analyte),
            r.SampleSource,
            SourceLabel(r.SampleSource),
            r.SampleLocation,
            r.Notes,
            r.Confidence,
            r.ImageUrl,
            r.PerformedBy,
            r.ControllerId ?? hw.ControllerCode,
            r.CameraId ?? hw.CameraCode,
            ReadLogs(r.StepLogJson));
    }

    private static int ProgressOf(WaterAnalysisRun r)
    {
        if (r.Status is "completed" or "cancelled") return 100;
        var step = Math.Clamp(r.CurrentStep, 1, TotalSteps);
        return (int)Math.Round((step - 1) * 100.0 / TotalSteps);
    }

    private static int? RemainingSeconds(WaterAnalysisRun r)
    {
        if (r.Status != "running") return null;
        var hold = HoldForStep(r.CurrentStep, r.Analyte);
        var left = (int)Math.Ceiling((r.LastStepAt + hold - DateTime.UtcNow).TotalSeconds);
        return Math.Max(0, left);
    }

    private static TimeSpan HoldForStep(int step, string? analyte)
    {
        if (step == 3)
        {
            var assay = Assays.FirstOrDefault(a => a.Analyte == NormalizeAnalyte(analyte));
            if (assay?.ReactionTimeSeconds is int sec and > 0)
                return TimeSpan.FromSeconds(sec);
        }

        return DefaultStepHold;
    }

    private static string SessionStateOf(WaterAnalysisRun r) => r.Status switch
    {
        "completed" => "COMPLETED",
        "failed" => "FAILED",
        "cancelled" => "CANCELLED",
        "running" => r.CurrentStep switch
        {
            1 => "RUNNING",
            2 => "RUNNING",
            3 => "WAITING_REACTION",
            4 => "RUNNING",
            5 => "CAPTURING",
            6 => "AI_PROCESSING",
            7 => "SAVING",
            8 => "CLEANING",
            _ => "RUNNING"
        },
        _ => "IDLE"
    };

    public static string StepLabel(int step)
    {
        var i = Math.Clamp(step, 1, TotalSteps) - 1;
        return StepLabels[i];
    }

    private static string DoneLabel(int step) => step switch
    {
        1 => "Lấy mẫu hoàn tất",
        2 => "Thuốc thử #1 hoàn tất",
        3 => "Reaction wait completed",
        4 => "Thuốc thử #2 hoàn tất",
        5 => "Camera image captured",
        6 => "AI prediction completed",
        7 => "Result saved",
        8 => "Drain & clean completed",
        _ => StepLabel(step)
    };

    private IReadOnlyList<WaterAnalysisMetricDto> MetricsOf(WaterAnalysisRun r) =>
    [
        Metric("ph", "pH", r.Ph, ""),
        Metric("nh3", "NH3", r.Nh3, "mg/L"),
        Metric("no2", "NO2", r.No2, "mg/L"),
        Metric("no3", "NO3", r.No3, "mg/L")
    ];

    private static WaterAnalysisMetricDto Metric(
        string code, string label, decimal? value, string unit)
    {
        var thr = Thresholds.FirstOrDefault(t => t.Code == code);
        if (value is null)
            return new(code, label, null, unit, "pending", "Chưa có", thr?.Display);
        var (st, lb) = Evaluate(code, value.Value);
        return new(code, label, value, unit, st, lb, thr?.Display);
    }

    private static (string Status, string Label) Evaluate(string code, decimal v)
    {
        var t = Thresholds.FirstOrDefault(x => x.Code == code);
        if (t is null) return ("NORMAL", "Bình thường");

        var inRange = (!t.Min.HasValue || v >= t.Min) && (!t.Max.HasValue || v <= t.Max);
        if (inRange)
            return code == "ph" ? ("GOOD", "Tốt") : ("NORMAL", "Bình thường");

        var inWarn = (!t.WarningMin.HasValue || v >= t.WarningMin)
                     && (!t.WarningMax.HasValue || v <= t.WarningMax);
        if (inWarn) return ("MONITORING", "Theo dõi");

        var far = (t.Max.HasValue && v > t.Max.Value * 2m)
                  || (t.Min.HasValue && v < t.Min.Value * 0.7m);
        return far ? ("CRITICAL", "Nguy hiểm") : ("WARNING", "Cảnh báo");
    }

    private async Task<WaterAnalysisRun?> ActiveRunAsync(Guid areaId, CancellationToken ct) =>
        (await _uow.WaterAnalysisRuns.FindAsync(
                r => r.FarmingAreaId == areaId && r.Status == "running", ct))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();

    private async Task<string> NextTestCodeAsync(Guid areaId, DateTime now, CancellationToken ct)
    {
        var prefix = $"TEST-{now:yyyyMMdd}-";
        var count = (await _uow.WaterAnalysisRuns.FindAsync(
                r => r.FarmingAreaId == areaId && r.StartedAt >= now.Date, ct))
            .Count();
        return $"{prefix}{(count + 1):000}";
    }

    private async Task<string> ResolveUserNameAsync(CancellationToken ct)
    {
        try
        {
            var user = await _uow.Users.GetByIdAsync(_currentUser.UserId, ct);
            if (user is not null && !string.IsNullOrWhiteSpace(user.FullName))
                return user.FullName.Trim();
        }
        catch
        {
            // JWT without user row
        }

        return "Chủ trại";
    }

    private async Task<HardwareState> ResolveHardwareAsync(Guid areaId, CancellationToken ct)
    {
        var devices = (await _uow.Devices.FindAsync(d => d.FarmingAreaId == areaId, ct))
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

        var analyzer = devices.FirstOrDefault(d =>
            TypeHas(d, "water") || TypeHas(d, "analyzer"));
        var controller = analyzer
            ?? devices.FirstOrDefault(d => TypeHas(d, "esp32"))
            ?? devices.FirstOrDefault(d => !TypeHas(d, "camera"));
        var camera = devices.FirstOrDefault(d =>
            TypeHas(d, "camera")
            || (d.DeviceCode ?? "").StartsWith("CAM", StringComparison.OrdinalIgnoreCase));

        var controllerOnline = controller?.Status == DeviceStatus.Online;
        var cameraOnline = camera?.Status == DeviceStatus.Online;
        return new HardwareState(
            controllerOnline,
            cameraOnline,
            true,
            controller?.DeviceCode,
            camera?.DeviceCode,
            controller?.FirmwareVersion,
            null);
    }

    private static bool TypeHas(Device d, string kind) =>
        (d.DeviceType ?? "").Contains(kind, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAnalyte(string? raw)
    {
        var v = (raw ?? "NO2").Trim().ToUpperInvariant();
        return v switch
        {
            "PH" or "PH_CHEM" => "PH",
            "NH3" or "AMMONIA" => "NH3",
            "NO3" or "NITRATE" => "NO3",
            _ => "NO2"
        };
    }

    private static string? NormalizeSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = raw.Trim().ToUpperInvariant().Replace(' ', '_');
        return SourceOptions.Any(s => s.Code == v) ? v : null;
    }

    private static string SourceLabel(string? code)
    {
        var n = NormalizeSource(code) ?? "RAS_RETURN";
        return SourceOptions.First(s => s.Code == n).Label;
    }

    private static string AnalyteLabel(string? analyte) => NormalizeAnalyte(analyte) switch
    {
        "PH" => "pH",
        "NH3" => "NH3",
        "NO3" => "NO3",
        _ => "NO2"
    };

    private static List<WaterAnalysisStepLogDto> ReadLogs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<WaterAnalysisStepLogDto>>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string WriteLogs(IReadOnlyList<WaterAnalysisStepLogDto> logs) =>
        JsonSerializer.Serialize(logs);

    private sealed record HardwareState(
        bool ControllerOnline,
        bool CameraOnline,
        bool AiReady,
        string? ControllerCode,
        string? CameraCode,
        string? FirmwareVersion,
        string? SystemError);

    private sealed class HardwareSnapshot
    {
        public string? ControllerId { get; set; }
        public string? SamplePumpId { get; set; }
        public string? ReagentPump1Id { get; set; }
        public string? ReagentPump2Id { get; set; }
        public string? DrainValveId { get; set; }
        public string? CameraId { get; set; }
        public string? AiModelId { get; set; }
    }

    private static string HardwareSnapshotJson(string analyte, HardwareState hw)
    {
        var prefix = analyte.Equals("NO2", StringComparison.OrdinalIgnoreCase) ? "NO2" : analyte;
        return JsonSerializer.Serialize(new HardwareSnapshot
        {
            ControllerId = hw.ControllerCode,
            SamplePumpId = "PUMP-SAMPLE-01",
            ReagentPump1Id = $"PUMP-{prefix}-01",
            ReagentPump2Id = $"PUMP-{prefix}-02",
            DrainValveId = "VALVE-DRAIN-01",
            CameraId = hw.CameraCode,
            AiModelId = Assays.FirstOrDefault(a => a.Analyte == analyte)?.AiModelId
        });
    }

    private static HardwareSnapshot ReadHardware(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new HardwareSnapshot();
        try
        {
            return JsonSerializer.Deserialize<HardwareSnapshot>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new HardwareSnapshot();
        }
        catch
        {
            return new HardwareSnapshot();
        }
    }

    private static string RunStatusLabel(string status) => status.ToLowerInvariant() switch
    {
        "completed" => "Hoàn thành",
        "running" => "Đang chạy",
        "failed" => "Thất bại",
        "cancelled" => "Đã hủy",
        _ => status
    };

    private static (string Level, string Label) ConfidenceBand(decimal? confidence, decimal? min)
    {
        if (confidence is null) return ("none", "Không sử dụng AI");
        var pct = confidence.Value;
        if (pct >= 0.90m) return ("high", "Độ tin cậy cao");
        if (pct >= 0.70m) return ("medium", "Độ tin cậy trung bình");
        return ("low", "Độ tin cậy thấp");
    }

    private static IReadOnlyList<WaterAnalysisColorSwatchDto> ColorReferenceOf(
        string code, decimal? value)
    {
        // Bộ màu tham chiếu theo assay — không tính toán hóa học ở UI.
        var set = code switch
        {
            "no2" => new (string Hex, decimal Value)[]
            {
                ("#F8D7DD", 0.00m),
                ("#F4B6C2", 0.02m),
                ("#E86B86", 0.05m),
                ("#D6456A", 0.08m),
                ("#F3C4CE", 0.10m)
            },
            "nh3" => new (string Hex, decimal Value)[]
            {
                ("#F7E7C8", 0.00m),
                ("#F3D08C", 0.03m),
                ("#E8B84A", 0.05m),
                ("#D4A017", 0.08m),
                ("#C08A00", 0.12m)
            },
            _ => Array.Empty<(string Hex, decimal Value)>()
        };
        if (set.Length == 0) return [];
        var selected = -1;
        if (value is not null)
        {
            selected = 0;
            var best = Math.Abs(set[0].Value - value.Value);
            for (var i = 1; i < set.Length; i++)
            {
                var d = Math.Abs(set[i].Value - value.Value);
                if (d < best)
                {
                    best = d;
                    selected = i;
                }
            }
        }

        return set.Select((s, i) => new WaterAnalysisColorSwatchDto(i, s.Hex, s.Value, i == selected))
            .ToList();
    }

    private static IReadOnlyList<WaterAnalysisHardwareItemDto> HardwareItems(
        HardwareSnapshot snap,
        HardwareState live,
        IReadOnlyList<WaterAnalysisStationPartDto> station,
        WaterAnalysisAssayDto assay)
    {
        WaterAnalysisStationPartDto? Part(string code) =>
            station.FirstOrDefault(p => p.Code == code);

        WaterAnalysisHardwareItemDto Item(
            string code, string label, string? id, bool clickable)
        {
            var p = Part(code);
            var ready = p?.Ready ?? false;
            return new(
                code,
                label,
                string.IsNullOrWhiteSpace(id) ? null : id,
                p?.State ?? (ready ? "READY" : "UNKNOWN"),
                p?.StateLabel ?? (ready ? "Sẵn sàng" : "Không xác định"),
                ready,
                clickable);
        }

        return
        [
            Item("controller", "Controller", snap.ControllerId ?? live.ControllerCode, true),
            Item("samplePump", "Bơm nước mẫu", snap.SamplePumpId, false),
            Item("reagentPump1", "Bơm thuốc thử NO2 #1", snap.ReagentPump1Id, false),
            Item("reagentPump2", "Bơm thuốc thử NO2 #2", snap.ReagentPump2Id, false),
            Item("drainValve", "Van xả", snap.DrainValveId, false),
            Item("camera", "Camera phân tích", snap.CameraId ?? live.CameraCode, true),
            Item("ai", "AI Analysis", snap.AiModelId ?? assay.AiModelId, false)
        ];
    }

    private static IReadOnlyList<WaterAnalysisProcessStepDto> BuildProcessSteps(
        WaterAnalysisRun run,
        IReadOnlyList<WaterAnalysisStepLogDto> logs,
        WaterAnalysisAssayDto assay,
        decimal? result)
    {
        string Detail(int i) => i switch
        {
            1 => assay.SampleVolumeMl is null
                ? "Bơm nước mẫu theo cấu hình assay"
                : $"Bơm {assay.SampleVolumeMl} mL nước mẫu",
            2 => string.IsNullOrWhiteSpace(assay.Reagent1Dose)
                ? "Bơm thuốc thử #1 theo cấu hình assay"
                : $"Bơm thuốc thử #1 ({assay.Reagent1Dose})",
            3 => assay.ReactionTimeSeconds is null
                ? "Chờ phản ứng theo cấu hình assay"
                : $"Thời gian chờ: {assay.ReactionTimeSeconds} giây",
            4 => string.IsNullOrWhiteSpace(assay.Reagent2Dose)
                ? "Bơm thuốc thử #2 theo cấu hình assay"
                : $"Bơm thuốc thử #2 ({assay.Reagent2Dose})",
            5 => "Chụp ảnh trong buồng thử",
            6 => result is null
                ? "AI phân tích màu mẫu"
                : $"{AnalyteLabel(run.Analyte)} = {result} {(run.Analyte == "PH" ? "" : "mg/L")}"
                  + (run.Confidence is null ? "" : $" (confidence {(run.Confidence * 100):0}%)"),
            7 => "Lưu vào hệ thống CrabSense",
            8 => "Xả buồng thử",
            _ => StepLabels[Math.Clamp(i, 1, TotalSteps) - 1]
        };

        var failedAt = run.Status == "failed" ? Math.Clamp(run.CurrentStep, 1, TotalSteps) : 0;
        var list = new List<WaterAnalysisProcessStepDto>();
        for (var i = 1; i <= TotalSteps; i++)
        {
            var log = logs.LastOrDefault(l => l.Step == i);
            string status;
            if (run.Status == "failed" && i == failedAt)
                status = "FAILED";
            else if (run.Status == "cancelled" && i >= run.CurrentStep)
                status = i == TotalSteps ? "COMPLETED" : "CANCELLED";
            else if (run.Status == "running" && i == run.CurrentStep)
                status = "RUNNING";
            else if (run.Status is "completed" || i < run.CurrentStep || (run.Status != "running" && i <= run.CurrentStep && log is not null))
                status = "COMPLETED";
            else if (run.Status is "completed")
                status = "COMPLETED";
            else
                status = "PENDING";

            if (run.Status == "completed")
                status = "COMPLETED";

            var label = status switch
            {
                "COMPLETED" => "Hoàn thành",
                "RUNNING" => "Đang chạy",
                "FAILED" => "Thất bại",
                "CANCELLED" => "Đã hủy",
                "SKIPPED" => "Bỏ qua",
                _ => "Chờ"
            };

            list.Add(new(
                i,
                StepCommands[i - 1],
                StepLabels[i - 1],
                status,
                label,
                log?.At,
                Detail(i),
                run.Status == "failed" && i == failedAt ? "STEP_FAILED" : log?.ErrorCode,
                run.Status == "failed" && i == failedAt ? run.Error : null));
        }

        return list;
    }
}
