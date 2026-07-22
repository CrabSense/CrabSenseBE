namespace CrabSenseBE.Application.DTOs.Dashboard;

// ============================================================
// FARM SUMMARY
// ============================================================

public record FarmSummaryDto
(
    int TotalAreas,
    int TotalRows,
    int TotalBoxes,
    int TotalCrabs,
    int TotalCrabLots
);

// ============================================================
// BOX UTILIZATION
// ============================================================

public record BoxUtilizationDto
(
    int Occupied,
    int Empty,
    decimal UtilizationRate
);

// ============================================================
// CRAB STATUS (loại trừ nhau)
// ============================================================

public record CrabStatusDto
(
    int Alive,        // MoltedAt == null && (Alive || Quarantined)
    int Molting,      // Status == Molting && MoltedAt == null
    int Molted,       // MoltedAt != null
    int Harvested,    // Status == Harvested
    int Dead          // Status == Dead
);

// ============================================================
// RECENT ACTIVITY
// ============================================================

public record RecentActivityDto
(
    int DeathsLast7Days,
    int HarvestsLast7Days,
    int ActiveAlerts
);

// ============================================================
// FROZEN INVENTORY SUMMARY
// ============================================================

public record FrozenInventorySummaryDto
(
    int TotalLots,
    decimal TotalWeightKg,
    int ExpiringSoon   // ExpiryDate <= now + 30 ngày
);

// ============================================================
// CHART DATA
// ============================================================

public record HarvestTrendPointDto
(
    DateTime Date,
    int Quantity,
    decimal WeightKg
);

public record MortalityTrendPointDto
(
    DateTime Date,
    int Count
);

public record BoxUtilizationByAreaDto
(
    Guid AreaId,
    string AreaName,
    int Occupied,
    int Empty
);

public record MortalityByCauseDto
(
    string Cause,
    int Count
);

// ============================================================
// CHARTS CONTAINER
// ============================================================

public record DashboardChartsDto
(
    List<HarvestTrendPointDto> HarvestTrend,
    List<MortalityTrendPointDto> MortalityTrend,
    List<BoxUtilizationByAreaDto> BoxUtilizationByArea,
    List<MortalityByCauseDto> MortalityByCause
);

// ============================================================
// DASHBOARD (root DTO)
// ============================================================

public record DashboardDto
(
    FarmSummaryDto FarmSummary,
    BoxUtilizationDto BoxUtilization,
    CrabStatusDto CrabStatus,
    RecentActivityDto RecentActivity,
    FrozenInventorySummaryDto FrozenInventory,
    DashboardChartsDto Charts
);