namespace CrabSenseBE.Application.DTOs.Reports;

/// <summary>
/// Bộ lọc cho báo cáo hiệu quả vận hành.
/// Lọc theo thời điểm thả cua (StockedAt).
/// </summary>
public record OperationalEfficiencyFilterDto
(
    DateTime? FromDate,
    DateTime? ToDate
);

/// <summary>
/// Báo cáo hiệu quả vận hành tổng hợp.
/// </summary>
public record OperationalEfficiencyDto
(
    int TotalCrabs,          // Tổng số cua đã thả trong khoảng thời gian
    int MoltedCrabs,         // Số cua đã lột xác (có MoltingRecord)
    int HarvestedCrabs,      // Số cua đã thu hoạch (CrabStatus.Harvested)
    int AliveCrabs,          // Số cua đang nuôi (Alive + Molting + Quarantined)
    int DeadCrabs,           // Số cua đã chết (CrabStatus.Dead)

    decimal MoltingRate,     // MoltedCrabs / TotalCrabs × 100
    decimal HarvestRate,     // HarvestedCrabs / TotalCrabs × 100
    decimal MortalityRate,   // DeadCrabs / TotalCrabs × 100
    decimal AliveRate        // AliveCrabs / TotalCrabs × 100
);