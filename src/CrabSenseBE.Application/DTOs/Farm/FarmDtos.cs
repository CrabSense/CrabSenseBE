namespace CrabSenseBE.Application.DTOs.Farm;

// Hierarchy on create (Swagger shows these required parents):
//   Area  → OwnerId from JWT (not in body)
//   Row   → FarmingAreaId
//   Box   → FarmingRowId  (Area auto from row)
//   Crab  → BoxId (+ auto Row/Area) + CrabLotId + CropBatchId

// --- FarmingArea (Khu) ---
public record FarmingAreaDto(
    Guid Id,
    Guid OwnerId,
    string? OwnerName,
    string Code,
    string Name,
    string? Location,
    string Address,
    string? Region,
    decimal? AreaSquareMeters,
    DateTime? EstablishedAt,
    DateTime CreatedAt,
    string? Description,
    string? AvatarUrl,
    string Status,
    bool IsActive,
    int RowCount,
    int BoxCount,
    int CrabCount,
    int HealthyBoxCount,
    int AlertBoxCount);

/// <summary>
/// Tạo khu. Code hệ thống tự sinh AREA-A01… — không gửi trong body.
/// OwnerId lấy từ JWT. Bắt buộc: Name.
/// </summary>
public record CreateFarmingAreaRequest(
    string Name,
    string? Location = null,
    decimal? AreaSquareMeters = null,
    string? Description = null,
    string? Status = null,
    string? Address = null,
    string? Region = null,
    DateTime? EstablishedAt = null,
    string? AvatarUrl = null);

/// <summary>Sửa khu. Code không đổi.</summary>
public record UpdateFarmingAreaRequest(
    string Name,
    string? Location = null,
    decimal? AreaSquareMeters = null,
    string? Description = null,
    string? Status = null,
    bool? IsActive = null,
    string? Address = null,
    string? Region = null,
    DateTime? EstablishedAt = null,
    string? AvatarUrl = null);

public record NextFarmCodeDto(string Code);

public record FarmAvatarDto(
    string Url,
    string StorageKey,
    string FileName,
    string Provider,
    long SizeBytes);

/// <summary>Area list filter. PageSize null = GET ALL.</summary>
public record FarmingAreaFilter(
    string? Search = null,
    bool? IsActive = null,
    string? Status = null,
    int Page = 1,
    int? PageSize = null,
    Guid? OwnerId = null);

// --- FarmingRow (Dãy) ---
public record FarmingRowDto(
    Guid Id,
    Guid FarmingAreaId,
    string? AreaName,
    string? AreaLocation,
    string Code,
    string Name,
    string? Location,
    string? Description,
    int Capacity,
    int SortOrder,
    string Status,
    bool IsActive,
    int BoxCount,
    int CrabCount,
    int HealthyBoxCount,
    int AlertBoxCount);

/// <summary>
/// Tạo dãy. Code hệ thống tự sinh DAY-A01… — không gửi trong body.
/// Required: FarmingAreaId + Name. Capacity = số hộp tối đa (0 = không giới hạn); không tạo hộp sẵn.
/// </summary>
public record CreateFarmingRowRequest(
    /// <summary>Parent khu id (GET /api/farming-areas).</summary>
    Guid FarmingAreaId,
    string Name,
    string? Location = null,
    /// <summary>Số hộp tối đa. 0 = không giới hạn. Không tạo hộp khi tạo dãy.</summary>
    int Capacity = 0,
    string? Description = null,
    string? Status = null,
    int? SortOrder = null);

/// <summary>Sửa dãy. Code không đổi.</summary>
public record UpdateFarmingRowRequest(
    string Name,
    string? Location = null,
    int? Capacity = null,
    string? Description = null,
    string? Status = null,
    bool? IsActive = null,
    int? SortOrder = null);

public record NextRowCodeDto(string Code);

/// <summary>Row list filter. PageSize null = GET ALL.</summary>
public record FarmingRowFilter(
    Guid? FarmingAreaId = null,
    string? Search = null,
    bool? IsActive = null,
    string? Status = null,
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
    bool IsOccupied,
    string? DisplayName = null,
    string? AreaCode = null,
    string? RowCode = null,
    Guid? CrabId = null,
    string? CrabTag = null,
    string? CrabMoltingStage = null,
    string? CrabStatus = null,
    /// <summary>empty | normal | premolt | molting | softshell | problem</summary>
    string? CrabCondition = null,
    int AlertCount = 0,
    string? AiSummary = null);

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
    IReadOnlyList<string> ImageUrls,
    string? Code = null,
    string? QrCode = null,
    string? CrabType = null,
    string? Gender = null,
    decimal? InitialWeightGram = null,
    decimal? CarapaceWidthMm = null,
    string? InitialCondition = null,
    string? Notes = null,
    string? Condition = null,
    string? Status = null,
    string? AiPrediction = null,
    decimal? AiConfidence = null,
    decimal? CarapaceLengthMm = null,
    string? RowName = null,
    string? RowCode = null,
    string? AreaName = null,
    string? AreaCode = null);

public record NextCrabCodeDto(string Code, string QrCode);

public record CrabStatusHistoryDto(
    Guid Id,
    Guid CrabId,
    string? OldCondition,
    string NewCondition,
    string? OldStatus,
    string NewStatus,
    DateTime ChangedAt,
    string Source,
    string? Reason);

public record CrabWeightHistoryDto(
    Guid Id,
    Guid CrabId,
    decimal WeightGram,
    DateTime MeasuredAt,
    string Source,
    string? Notes);

public record CrabAiAnalysisDto(
    Guid Id,
    Guid CrabId,
    Guid? BoxId,
    string Prediction,
    decimal Confidence,
    string? ActivityLevel,
    string? AnomalyNote,
    string? MediaUrl,
    string? ModelVersion,
    DateTime AnalyzedAt);

public record CrabHarvestHistoryDto(
    Guid Id,
    Guid CrabId,
    Guid? HarvestLineId,
    DateTime HarvestedAt,
    decimal? WeightGram,
    string? Grade,
    string? Notes);

/// <summary>
/// Place crab. Required: CrabLotId + BoxId
/// (or autoAssignEmptyBox + farmingRowId/farmingAreaId).
/// Code / QR auto. Row/Area auto-fill from the box.
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
    IReadOnlyList<string>? ImageUrls = null,
    DateTime? StockedAt = null,
    string? CrabType = null,
    string? Gender = null,
    decimal? InitialWeightGram = null,
    decimal? CarapaceWidthMm = null,
    string? InitialCondition = null,
    string? Notes = null,
    string? Condition = null,
    decimal? CarapaceLengthMm = null);

public record UpdateCrabRequest(
    string? MoltingStage,
    decimal? WeightGram,
    bool IsAlive,
    DateTime? MoltedAt,
    IReadOnlyList<string>? ImageUrls = null,
    string? Condition = null,
    string? Notes = null,
    string? CrabType = null,
    string? Gender = null,
    decimal? CarapaceWidthMm = null,
    decimal? CarapaceLengthMm = null);
