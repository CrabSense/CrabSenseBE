namespace CrabSenseBE.Application.DTOs.Farm;

// --- FarmingArea ---
public record FarmingAreaDto(Guid Id, string Name, string? Description, bool IsActive, int RowCount);
public record CreateFarmingAreaRequest(string Name, string? Description);
public record UpdateFarmingAreaRequest(string Name, string? Description, bool IsActive);

// --- FarmingRow ---
public record FarmingRowDto(Guid Id, Guid FarmingAreaId, string Name, int Capacity, bool IsActive, int BoxCount);
public record CreateFarmingRowRequest(Guid FarmingAreaId, string Name, int Capacity);

// --- Box ---
public record BoxDto(Guid Id, Guid FarmingRowId, string Code, string? Status, bool IsOccupied);
public record CreateBoxRequest(Guid FarmingRowId, string Code);

// --- Crab ---
public record CrabDto(
    Guid Id, Guid BoxId, Guid? CrabLotId,
    string? Tag, decimal? WeightGram, string? MoltingStage,
    bool IsAlive, DateTime? MoltedAt
);
public record CreateCrabRequest(Guid BoxId, Guid? CrabLotId, string? Tag, decimal? WeightGram);
public record UpdateCrabRequest(string? MoltingStage, decimal? WeightGram, bool IsAlive, DateTime? MoltedAt);

// --- CrabLot ---
public record CrabLotDto(Guid Id, string LotCode, DateTime ImportDate, int Quantity, decimal? AverageWeightGram, string? SupplierName);
public record CreateCrabLotRequest(string LotCode, DateTime ImportDate, int Quantity, decimal? AverageWeightGram, string? SupplierName);
