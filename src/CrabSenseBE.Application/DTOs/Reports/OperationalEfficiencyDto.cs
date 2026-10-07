namespace CrabSenseBE.Application.DTOs.Reports;

/// <summary>
/// Bộ lọc cho báo cáo hiệu quả vận hành.
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
    int TotalCrabs,
    int AliveCrabs,          // Cua đang nuôi, chưa lột
    int MoltingCrabs,        // Cua đang trong giai đoạn lột
    int MoltedCrabs,         // Cua đã lột xong
    int HarvestedCrabs,
    int DeadCrabs,
    int UnclassifiedCrabs,

    decimal SurvivalRate,    // (Alive + Molting) / Total × 100
    decimal MoltingRate,     // Molting / Total × 100
    decimal HarvestRate,     // Harvested / Total × 100
    decimal MortalityRate,   // Dead / Total × 100
    decimal AliveRate,       // Alive / Total × 100 (chỉ cua chưa lột)

    decimal HealthScore      // Điểm tổng hợp 0-100
);