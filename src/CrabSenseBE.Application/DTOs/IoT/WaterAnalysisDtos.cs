namespace CrabSenseBE.Application.DTOs.IoT;

public record WaterAnalysisMetricDto(
    string Code,
    string Label,
    decimal? Value,
    string Unit,
    string Status,
    string StatusLabel);

public record WaterAnalysisStationPartDto(
    string Code,
    string Label,
    bool Ready,
    string StateLabel);

public record WaterAnalysisRunDto(
    Guid Id,
    string Status,
    int CurrentStep,
    string StepLabel,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<WaterAnalysisMetricDto> Metrics,
    string? Error);

public record WaterAnalysisSnapshotDto(
    WaterAnalysisRunDto? Latest,
    WaterAnalysisRunDto? Active,
    IReadOnlyList<WaterAnalysisStationPartDto> Station);
