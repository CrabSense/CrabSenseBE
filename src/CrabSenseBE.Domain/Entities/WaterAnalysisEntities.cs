using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Một lần phân tích hóa học (thuốc thử + camera + AI màu).
/// Không phải mẫu realtime từ cảm biến TDS/nhiệt/mặn.
/// </summary>
public class WaterAnalysisRun : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    /// <summary>running | completed | failed | cancelled</summary>
    public string Status { get; set; } = "running";
    /// <summary>1 lấy mẫu … 8 xả &amp; làm sạch</summary>
    public int CurrentStep { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastStepAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public decimal? Ph { get; set; }
    public decimal? Nh3 { get; set; }
    public decimal? No2 { get; set; }
    public decimal? No3 { get; set; }

    public string? ImageUrl { get; set; }
    public string? Error { get; set; }
    public string Source { get; set; } = "colorimetric-ai";

    public string? Analyte { get; set; }
    public string? SampleSource { get; set; }
    public string? SampleLocation { get; set; }
    public string? Notes { get; set; }
    public string? TestCode { get; set; }
    public decimal? Confidence { get; set; }
    public string? PerformedBy { get; set; }
    public string? StepLogJson { get; set; }
    public string? ControllerId { get; set; }
    public string? CameraId { get; set; }
    public string? HardwareJson { get; set; }
    /// <summary>simulation | hardware. Hardware chỉ tiến bước khi controller ACK.</summary>
    public string ExecutionMode { get; set; } = "simulation";
    public string? AwaitingEvent { get; set; }
    public DateTime? StepDeadlineAt { get; set; }
    public DateTime? ResultRecordedAt { get; set; }
    public DateTime? CleaningConfirmedAt { get; set; }

    public Guid? BatchId { get; set; }
    public decimal? MeasuredValue { get; set; }
    public string? MeasuredUnit { get; set; }
    public Guid? CalibrationId { get; set; }
    public Guid? SampleMediaId { get; set; }
    public string? ColorEvidenceJson { get; set; }
    public string? SampleSha256 { get; set; }
    public FarmingArea? FarmingArea { get; set; }
}

public sealed class WaterColorCalibration : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    public Guid CameraDeviceId { get; set; }
    public string Analyte { get; set; } = "";
    public string KitName { get; set; } = "";
    public string ReagentLot { get; set; } = "";
    public string Unit { get; set; } = "";
    public string ProtocolJson { get; set; } = "";
    public string CurveJson { get; set; } = "";
    public string? AiAlgorithm { get; set; }
    public string? AiCalibrationId { get; set; }
    public string? KitRevision { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AiCalibrationToken { get; set; }
    public string ValidationJson { get; set; } = "";
    public DateTime ValidUntil { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? QualityCheckDueAt { get; set; }
    public Guid CreatedBy { get; set; }
}

public sealed class WaterAnalysisBatch : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    public string Trigger { get; set; } = "";
    public string Status { get; set; } = "waiting_samples";
    public string TargetsJson { get; set; } = "";
    public string SampleSource { get; set; } = "";
    public Guid? ScheduleId { get; set; }
    public DateTime? ScheduledFor { get; set; }
    public Guid? RequestedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
}
public sealed class WaterAnalysisSchedule : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    public string TargetsJson { get; set; } = "";
    public string SampleSource { get; set; } = "";
    public int IntervalMinutes { get; set; }
    public bool Enabled { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastOutcome { get; set; }
}

public sealed class WaterAssayChannel : BaseEntity
{
    public Guid BatchId { get; set; }
    public Guid CalibrationId { get; set; }
    public string Analyte { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileJson { get; set; } = "";
    public string ProtocolJson { get; set; } = "";
    public string State { get; set; } = "waiting";
    public int StepIndex { get; set; }
    public DateTime StepStartedAt { get; set; }
    public DateTime? ReactionReadyAt { get; set; }
    public Guid? CapturedMediaId { get; set; }
    public string? Failure { get; set; }
    public string EventsJson { get; set; } = "[]";
}

public sealed class WaterCalibrationCheck : BaseEntity
{
    public Guid CalibrationId { get; set; }
    public Guid MediaId { get; set; }
    public Guid PerformedBy { get; set; }
    public string Status { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Expected { get; set; }
    public decimal Estimated { get; set; }
    public decimal AbsoluteError { get; set; }
    public decimal MaxError { get; set; }
    public string ImageSha256 { get; set; } = "";
    public string EvidenceJson { get; set; } = "";
}

public sealed class WaterTrainingSample : BaseEntity
{
    public Guid CalibrationId { get; set; }
    public Guid FarmingAreaId { get; set; }
    public Guid MediaId { get; set; }
    public string ImageSha256 { get; set; } = "";
    public string PreparationId { get; set; } = "";
    public string Split { get; set; } = "";
    public string LabelSource { get; set; } = "";
    public string ReferenceId { get; set; } = "";
    public decimal Concentration { get; set; }
    public int ReactionSeconds { get; set; }
    public decimal SalinityPpt { get; set; }
    public Guid ReviewedBy { get; set; }
    public DateTime ReviewedAt { get; set; }
    public DateTime? WithdrawnAt { get; set; }
}
