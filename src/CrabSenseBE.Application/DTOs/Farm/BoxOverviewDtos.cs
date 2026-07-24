namespace CrabSenseBE.Application.DTOs.Farm;

/// <summary>Water snapshot shared at farming-area RAS level (no per-box sensors yet).</summary>
public record BoxWaterSnapshotDto(
    decimal? Temperature,
    decimal? Ph,
    decimal? DissolvedOxygen,
    DateTime? MeasuredAt,
    IReadOnlyList<string> Alarms);

/// <summary>Device online summary for the box's farming area.</summary>
public record BoxDeviceSnapshotDto(
    bool IsOnline,
    int OnlineCount,
    int TotalCount);

/// <summary>AI health score for a single box card.</summary>
public record BoxHealthScoreDto(
    int Score,
    double AiConfidence,
    string Trend,
    string StatusLabel,
    string StatusLevel,
    string Explanation);

/// <summary>Relative layout node for Farm Digital Twin (computed from row/box order).</summary>
public record BoxMapLayoutDto(
    double GridX,
    double GridY,
    int RowIndex,
    int BoxIndexInRow);

/// <summary>Optional AI recommendation attached to a box.</summary>
public record BoxAiTipDto(
    string Id,
    string Title,
    string Description,
    string Priority,
    int ConfidencePercentage,
    bool HasRecommendation);

/// <summary>Enriched box card for Mobile Boxes tab.</summary>
public record BoxOverviewItemDto(
    Guid Id,
    string Code,
    string Name,
    string QrCode,
    Guid FarmingAreaId,
    string FarmName,
    Guid FarmingRowId,
    string? RowName,
    string? AreaName,
    string? Status,
    bool IsOccupied,
    string HealthStatus,
    BoxHealthScoreDto Health,
    int CrabCount,
    string? CrabType,
    string? Batch,
    string? MoltingStage,
    BoxWaterSnapshotDto Water,
    BoxDeviceSnapshotDto Devices,
    int AlertCount,
    string? LatestAlertTitle,
    DateTime? LatestAlertAt,
    BoxAiTipDto? AiRecommendation,
    BoxMapLayoutDto Layout,
    DateTime LastUpdated,
    DateTime? ExpectedHarvestAt,
    bool WaterTestDue,
    bool VideoDue,
    string Priority);

/// <summary>Farm-level summary counts for Boxes header.</summary>
public record BoxesFarmSummaryDto(
    int Total,
    int Healthy,
    int Warning,
    int Critical,
    int Offline,
    int WithAiRecommendation);

/// <summary>Full payload for GET /api/boxes/overview.</summary>
public record BoxesOverviewResponseDto(
    Guid? FarmingAreaId,
    string FarmName,
    BoxesFarmSummaryDto Summary,
    BoxWaterSnapshotDto AreaWater,
    BoxDeviceSnapshotDto AreaDevices,
    IReadOnlyList<BoxOverviewItemDto> Items,
    DateTime SyncedAt);
