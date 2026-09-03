namespace CrabSenseBE.Application.DTOs.Farm;

// Hierarchy on create (Swagger shows these required parents):
//   Area  → OwnerId from JWT (not in body)
//   Row   → FarmingAreaId
//   Box   → FarmingRowId  (Area auto from row)
//   Crab  → BoxId (+ auto Row/Area) + CrabLotId + CropBatchId

// --- FarmingArea ---
public record FarmingAreaDto(
    Guid Id,
    Guid OwnerId,
    string? OwnerName,
    string Name,
    string? Description,
    bool IsActive,
    int RowCount);

/// <summary>
/// Create khu. Body: Name (+ Description).
/// Owner is NOT in body — API sets OwnerId from the logged-in JWT user.
/// </summary>
public record CreateFarmingAreaRequest(string Name, string? Description);
public record UpdateFarmingAreaRequest(string Name, string? Description, bool IsActive);

/// <summary>Area list filter. PageSize null = GET ALL.</summary>
public record FarmingAreaFilter(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int? PageSize = null,
    Guid? OwnerId = null);

// --- FarmingRow ---
public record FarmingRowDto(
    Guid Id, Guid FarmingAreaId, string? AreaName, string Name, int Capacity, bool IsActive, int BoxCount);

/// <summary>Create dãy. Required: FarmingAreaId (khu) + Name. Capacity = số hộp tối đa; nếu &gt; 0 thì tạo luôn đúng số hộp đó.</summary>
public record CreateFarmingRowRequest(
    /// <summary>Parent khu id (GET /api/farming-areas).</summary>
    Guid FarmingAreaId,
    string Name,
    /// <summary>Số hộp của dãy. &gt; 0 → tạo sẵn đúng số hộp (BOX-xxxx). 0 → chưa tạo hộp (unlimited).</summary>
    int Capacity = 0);
public record UpdateFarmingRowRequest(string Name, int Capacity, bool IsActive);

/// <summary>Row list filter. PageSize null = GET ALL.</summary>
public record FarmingRowFilter(
    Guid? FarmingAreaId = null,
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int? PageSize = null);

// --- Box ---
public record BoxDto(
    Guid Id,
    Guid FarmingRowId,
    Guid FarmingAreaId,
    string? RowName,
    string? AreaName,
    string Code,
    string? Status,
    bool IsOccupied);

/// <summary>
/// Create hộp. Required: FarmingRowId (dãy).
/// FarmingAreaId auto-filled from that row (optional check only).
/// Code optional → auto BOX-0001…
/// </summary>
public record CreateBoxRequest(Guid FarmingRowId, Guid? FarmingAreaId = null, string? Code = null);

public record UpdateBoxRequest(string Code, string? Status, bool IsOccupied);
public record UpdateBoxStatusRequest(string Status, bool IsOccupied);

/// <summary>Empty box ready for stocking.</summary>
public record AvailableBoxDto(
    Guid Id,
    string Code,
    Guid FarmingRowId,
    Guid FarmingAreaId,
    string? RowName,
    string? AreaName,
    string? Status);

/// <summary>Row capacity summary (free slots / empty boxes).</summary>
public record RowAvailabilityDto(
    Guid FarmingRowId,
    Guid FarmingAreaId,
    string? RowName,
    string? AreaName,
    int Capacity,
    int BoxCount,
    int EmptyBoxCount,
    int FreeCapacitySlots);

/// <summary>Current empty boxes + suggested next + rows with space.</summary>
public record FarmAvailabilityDto(
    int EmptyBoxCount,
    int OccupiedBoxCount,
    AvailableBoxDto? SuggestedNextEmptyBox,
    IReadOnlyList<AvailableBoxDto> EmptyBoxes,
    IReadOnlyList<RowAvailabilityDto> Rows);

/// <summary>Box list filter. PageSize null = GET ALL.</summary>
public record BoxFilter(
    Guid? FarmingAreaId = null,
    Guid? FarmingRowId = null,
    string? Code = null,
    string? Status = null,
    bool? IsOccupied = null,
    int Page = 1,
    int? PageSize = null);

// --- Crab ---
public record CrabDto(
    Guid Id,
    Guid BoxId,
    string? BoxCode,
    Guid FarmingRowId,
    Guid FarmingAreaId,
    Guid CrabLotId,
    string? Tag,
    decimal? WeightGram,
    string? MoltingStage,
    bool IsAlive,
    DateTime? MoltedAt,
    DateTime StockedAt,
    IReadOnlyList<string> ImageUrls);

/// <summary>
/// Place crab. Required: CrabLotId + CropBatchId, and BoxId
/// (or autoAssignEmptyBox + farmingRowId/farmingAreaId).
/// Row/Area auto-fill from the box — do not invent parents.
/// </summary>
public record CreateCrabRequest(
    Guid CrabLotId,
    Guid? BoxId = null,
    Guid? FarmingRowId = null,
    Guid? FarmingAreaId = null,
    bool AutoAssignEmptyBox = false,
    string? Tag = null,
    decimal? WeightGram = null,
    string? MoltingStage = null,
    IReadOnlyList<string>? ImageUrls = null);

public record UpdateCrabRequest(
    string? MoltingStage,
    decimal? WeightGram,
    bool IsAlive,
    DateTime? MoltedAt,
    IReadOnlyList<string>? ImageUrls = null);
