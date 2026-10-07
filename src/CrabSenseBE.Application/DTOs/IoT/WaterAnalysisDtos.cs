namespace CrabSenseBE.Application.DTOs.IoT;

public record WaterAnalysisMetricDto(
    string Code,
    string Label,
    decimal? Value,
    string Unit,
    string Status,
    string StatusLabel,
    string? ThresholdLabel = null);

public record WaterAnalysisStationPartDto(
    string Code,
    string Label,
    bool Ready,
    string State,
    string StateLabel,
    string? Detail = null,
    string? LevelLabel = null);

public record WaterAnalysisStepLogDto(
    DateTime At,
    int Step,
    string Event,
    string Label,
    string? Status = null,
    string? Detail = null,
    string? ErrorCode = null);

public record WaterAnalysisRunDto(
    Guid Id,
    string Status,
    int CurrentStep,
    string StepLabel,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<WaterAnalysisMetricDto> Metrics,
    string? Error,
    string? TestCode = null,
    string? SessionState = null,
    int TotalSteps = 8,
    int ProgressPct = 0,
    int? RemainingSeconds = null,
    string? Analyte = null,
    string? AnalyteLabel = null,
    string? SampleSource = null,
    string? SampleSourceLabel = null,
    string? SampleLocation = null,
    string? Notes = null,
    decimal? Confidence = null,
    string? ImageUrl = null,
    string? PerformedBy = null,
    string? ControllerId = null,
    string? CameraId = null,
    IReadOnlyList<WaterAnalysisStepLogDto>? StepLogs = null);

public record WaterAnalysisThresholdDto(
    string Code,
    string Label,
    string Unit,
    decimal? Min,
    decimal? Max,
    decimal? WarningMin,
    decimal? WarningMax,
    string Display);

public record WaterAnalysisSampleDto(
    string AreaName,
    string AreaCode,
    string SampleSource,
    string SampleSourceLabel,
    string? SampleLocation,
    string? Notes);

public record WaterAnalysisAssayDto(
    string Analyte,
    string Label,
    bool Configured,
    decimal? SampleVolumeMl,
    int? ReactionTimeSeconds,
    string? Reagent1Dose,
    string? Reagent2Dose,
    string? AiModelId,
    decimal? AiConfidenceMin);

public record WaterAnalysisSourceOptionDto(string Code, string Label);

public record WaterAnalysisBlockerDto(string Code, string Label, string Reason);

public record WaterAnalysisTrendPointDto(
    DateTime Date,
    decimal Value,
    string Status,
    string StatusLabel,
    string? TestCode,
    Guid? RunId);

public record WaterAnalysisStepDefDto(int Index, string Label, string Command);

public record StartWaterAnalysisRequest(
    string? Analyte,
    string? SampleSource,
    string? SampleLocation,
    string? Notes);

public record UpdateWaterAnalysisSampleRequest(
    string? SampleSource,
    string? SampleLocation,
    string? Notes);

public record WaterAnalysisSnapshotDto(
    WaterAnalysisRunDto? Latest,
    WaterAnalysisRunDto? Active,
    IReadOnlyList<WaterAnalysisStationPartDto> Station,
    IReadOnlyList<WaterAnalysisRunDto>? History = null,
    IReadOnlyList<WaterAnalysisThresholdDto>? Thresholds = null,
    WaterAnalysisSampleDto? Sample = null,
    IReadOnlyList<WaterAnalysisAssayDto>? Assays = null,
    IReadOnlyList<WaterAnalysisSourceOptionDto>? SampleSources = null,
    IReadOnlyList<WaterAnalysisBlockerDto>? Blockers = null,
    bool CanStart = false,
    IReadOnlyList<WaterAnalysisTrendPointDto>? Trend = null,
    string? TrendAnalyte = null,
    IReadOnlyList<WaterAnalysisStepDefDto>? Steps = null,
    string? SystemError = null);

public record WaterAnalysisColorSwatchDto(
    int Index,
    string Hex,
    decimal? Value,
    bool Selected);

public record WaterAnalysisHardwareItemDto(
    string Code,
    string Label,
    string? DeviceId,
    string State,
    string StateLabel,
    bool Ready,
    bool Clickable);

public record WaterAnalysisProcessStepDto(
    int Index,
    string Key,
    string Label,
    string Status,
    string StatusLabel,
    DateTime? At,
    string? Detail,
    string? ErrorCode,
    string? ErrorMessage);

public record WaterAnalysisPreviousDto(
    string? TestCode,
    decimal? Value,
    string Unit,
    DateTime? At,
    decimal? Delta);

public record WaterAnalysisRelatedAlertDto(string Code, string Title);

public record WaterAnalysisDetailDto(
    Guid Id,
    string? TestCode,
    string Status,
    string StatusLabel,
    string Analyte,
    string AnalyteLabel,
    decimal? Result,
    string Unit,
    string Evaluation,
    string EvaluationLabel,
    string? ThresholdDisplay,
    decimal? OverThreshold,
    decimal? Confidence,
    string? ConfidenceLevel,
    string? ConfidenceLabel,
    bool UsedAi,
    string? ImageUrl,
    string? AreaName,
    string? AreaCode,
    string? SampleSource,
    string? SampleSourceLabel,
    string? SampleLocation,
    string? Notes,
    string Method,
    string? PerformedBy,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<WaterAnalysisHardwareItemDto> Hardware,
    IReadOnlyList<WaterAnalysisColorSwatchDto> ColorReference,
    string? ColorReferenceNote,
    IReadOnlyList<WaterAnalysisProcessStepDto> Steps,
    bool LegacySteps,
    string? InternalId,
    string? AiModelId,
    string? AiModelVersion,
    string? FirmwareVersion,
    string? ImagePath,
    WaterAnalysisPreviousDto? Previous,
    WaterAnalysisRelatedAlertDto? Alert,
    string? Error,
    string? FailedStep,
    bool CanRerun);
