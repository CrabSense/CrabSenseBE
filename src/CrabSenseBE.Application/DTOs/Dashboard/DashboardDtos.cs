namespace CrabSenseBE.Application.DTOs.Dashboard;

public record DashboardOverviewDto(
    int TotalBoxes,
    int TotalCrabs,
    int ActiveBoxes,
    int OpenAlerts,
    double IotOnlinePercentage,
    DateTime LastUpdated,
    DashboardKpiSummaryDto? KpiSummary  // KPI tổng quan
);

public record DashboardMetricsDto(
    int Score,
    string StatusLevel,
    string StatusLabel,
    double DeltaVsYesterday,
    DateTime LastAiUpdated,
    int WaterQualityScore,
    int CrabHealthScore,
    int DeviceStatusScore,
    string Explanation
);

public record AiRecommendationDto(
    string Id,
    string Type,
    string Title,
    string Description,
    string TargetBoxOrArea,
    int ConfidencePercentage,
    string Priority,
    string Reason,
    string OptimalTimeframe,
    string ExpectedImpact,
    bool HasActiveRecommendation
);

public record OperationTaskDto(
    string Id,
    string Title,
    string Target,
    DateTime Deadline,
    string Priority,
    bool IsCompleted
);

public record RecentActivityDto(
    string Id,
    string Title,
    string Description,
    string Type,
    DateTime Timestamp
);

/// <summary>KPI tóm tắt cho dashboard.</summary>
public record DashboardKpiSummaryDto(
    decimal SurvivalRate,
    decimal MoltingRate,
    decimal HarvestRate,
    decimal MortalityRate,
    decimal TotalHarvestWeightKg,
    decimal FrozenInventoryWeightKg
);
