public record MoltingReportDto
(
    int TotalCrabs,
    int MoltingCrabs,       // cua đang trong giai đoạn lột (CrabStatus.Molting)
    int MoltedCrabs,        // cua đã lột xong (có MoltingRecord)
    decimal MoltingRate,    // MoltingRate = MoltedCrabs / TotalCrabs × 100
    List<MoltingByAreaDto> ByAreas
);

public record MoltingByAreaDto
(
    Guid AreaId,
    string AreaName,
    int TotalCrabs,
    int MoltingCrabs,
    int MoltedCrabs,
    decimal MoltingRate
);