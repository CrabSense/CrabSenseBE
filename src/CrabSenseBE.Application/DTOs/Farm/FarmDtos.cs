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
    int AlertBoxCount,
    /// <summary>Ảnh bản đồ trại (Maps top-down). Null → FE dùng ảnh mặc định.</summary>
    string? MapImageUrl = null,
    /// <summary>Khung khu trên ảnh bản đồ, tỉ lệ 0–1 (null = FE tự neo).</summary>
    decimal? MapX1 = null,
    decimal? MapY1 = null,
    decimal? MapX2 = null,
    decimal? MapY2 = null,
    /// <summary>Hộp đang có cua (Đang nuôi).</summary>
    int OccupiedBoxCount = 0,
    /// <summary>Hộp Theo dõi (status watch/maintenance, chưa tới mức cảnh báo).</summary>
    int WatchBoxCount = 0,
    /// <summary>Hộp trống (không có cua).</summary>
    int EmptyBoxCount = 0,
    /// <summary>Cập nhật cuối của bản ghi khu (UpdatedAt ?? CreatedAt).</summary>
    DateTime? UpdatedAt = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>
/// Cập nhật vị trí khu trên ảnh bản đồ trại. Tất cả tỉ lệ 0–1 theo chiều rộng/cao ảnh.
/// Gửi null cho cả 4 toạ độ để xoá khung; MapImageUrl null = giữ nguyên, "" = xoá.
/// </summary>
public record UpdateAreaMapRequest(
    string? MapImageUrl = null,
    decimal? MapX1 = null,
    decimal? MapY1 = null,
    decimal? MapX2 = null,
    decimal? MapY2 = null);

/// <summary>Cập nhật tâm dãy/hộp trên ảnh bản đồ trại (tỉ lệ 0–1). Cả 2 null = xoá vị trí.</summary>
public record UpdateMapPointRequest(decimal? MapX = null, decimal? MapY = null);

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
    string? AvatarUrl = null,
    double? Latitude = null,
    double? Longitude = null);

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
    string? AvatarUrl = null,
    double? Latitude = null,
    double? Longitude = null);

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
    int AlertBoxCount,
    /// <summary>Tâm dãy trên ảnh bản đồ trại (tỉ lệ 0–1). Null = chưa đặt.</summary>
    decimal? MapX = null,
    decimal? MapY = null,
    /// <summary>Số hộp đang có cua.</summary>
    int OccupiedBoxCount = 0,
    /// <summary>Số hộp đang ở trạng thái Theo dõi (watch/maintenance, chưa tới cảnh báo).</summary>
    int WatchBoxCount = 0,
    /// <summary>Số hộp không có cua.</summary>
    int EmptyBoxCount = 0,
    DateTime? UpdatedAt = null,
    DateTime? CreatedAt = null);

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
    string? AiSummary = null,
    int CrabCount = 0,
    /// <summary>Thời điểm cua bắt đầu ở hộp hiện tại (allocation mở).</summary>
    DateTime? CrabInBoxSince = null,
    /// <summary>Thời điểm cập nhật tình trạng / AI gần nhất (UTC).</summary>
    DateTime? AiUpdatedAt = null,
    /// <summary>Thời điểm hộp trống gần nhất (khi không còn cua).</summary>
    DateTime? EmptySince = null,
    /// <summary>Tâm hộp trên ảnh bản đồ trại (tỉ lệ 0–1). Null = chưa đặt.</summary>
    decimal? MapX = null,
    decimal? MapY = null);

/// <summary>
/// Create hộp. Required: FarmingRowId (dãy).
/// FarmingAreaId auto-filled from that row (optional check only).
/// Code optional → auto BOX-0001…
/// </summary>
public record CreateBoxRequest(Guid FarmingRowId, Guid? FarmingAreaId = null, string? Code = null);

/// <summary>Tạo nhiều hộp trong dãy. Backend tự sinh mã BOX-0001… Quantity >= 1, không vượt sức chứa.</summary>
public record CreateBoxesBulkRequest(int Quantity = 1);

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
    string? AreaCode = null,
    string? LotCode = null,
    string? LotName = null,
    DateTime? ImportDate = null,
    DateTime? AiAnalyzedAt = null,
    string? AiRecommendation = null,
    string? AvatarUrl = null);

/// <summary>Hồ sơ vòng đời cua — gom snapshot + lịch sử, không ghi đè bảng Crabs.</summary>
public record CrabProfileDto(
    CrabDto Crab,
    CrabProfileLocationDto Location,
    CrabProfileLotDto Lot,
    CrabProfileAiDto Ai,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<CrabTimelineEventDto> Timeline,
    IReadOnlyList<CrabProfileAlertDto> Alerts);

public record CrabProfileLocationDto(
    Guid FarmingAreaId,
    Guid FarmingRowId,
    Guid BoxId,
    string? AreaName,
    string? AreaCode,
    string? RowName,
    string? RowCode,
    string? BoxCode);

public record CrabProfileLotDto(
    Guid Id,
    string LotCode,
    string? Name,
    DateTime? ImportDate);

public record CrabProfileAiDto(
    string? Prediction,
    decimal? Confidence,
    DateTime? AnalyzedAt,
    string? Recommendation,
    string? ActivityLevel,
    string? MediaUrl);

public record CrabTimelineEventDto(
    DateTime At,
    string Kind,
    string Title,
    string? Detail);

public record CrabProfileAlertDto(
    DateTime At,
    string Title,
    string? Detail,
    string Severity);

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
    string? Notes,
    decimal? CarapaceWidthMm = null,
    decimal? CarapaceLengthMm = null,
    string? RecordedByName = null,
    IReadOnlyList<string>? PhotoUrls = null);

public record RecordCrabWeightRequest(
    DateTime? MeasuredAt,
    decimal WeightGram,
    decimal? CarapaceWidthMm = null,
    decimal? CarapaceLengthMm = null,
    string? Notes = null,
    string? RecordedByName = null,
    IReadOnlyList<string>? PhotoUrls = null,
    string? Source = null);

public record UpdateCrabWeightRequest(string? Notes);

public record CrabGrowthMoltDto(
    IReadOnlyList<CrabWeightHistoryDto> Measurements,
    IReadOnlyList<MoltingRecordDto> Molts,
    decimal? CurrentWeightGram,
    decimal? CurrentWidthMm,
    decimal? CurrentLengthMm);

/// <summary>Nhật ký vòng đời một cá thể cua (audit + timeline).</summary>
public record CrabLifecycleEventsDto(
    IReadOnlyList<CrabLifecycleEventDto> Items,
    int Total,
    bool HasMore,
    CrabLifecycleSummaryDto Summary);

public record CrabLifecycleSummaryDto(
    int Total,
    int Feeding,
    int Growth,
    int Molt,
    int Health,
    int Transfer,
    int Ai,
    int Alert,
    int Harvest,
    int System,
    int Note);

public record CrabLifecycleEventDto(
    string Id,
    Guid CrabId,
    string CrabCode,
    string EventType,
    DateTime OccurredAt,
    string Title,
    string Summary,
    CrabLifecycleLocationDto? Location,
    CrabLifecycleActorDto Actor,
    string Source,
    string? CameraId,
    IReadOnlyDictionary<string, CrabLifecycleChangeDto>? Changes,
    IReadOnlyDictionary<string, object?>? Metadata,
    IReadOnlyList<string> MediaUrls,
    string? Note,
    string? Severity);

public record CrabLifecycleLocationDto(
    Guid? FarmAreaId,
    string? FarmAreaCode,
    Guid? RowId,
    string? RowCode,
    Guid? BoxId,
    string? BoxCode);

public record CrabLifecycleActorDto(string Type, Guid? Id, string Name);

public record CrabLifecycleChangeDto(object? Before, object? After);

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

public record CreateCrabBulkItem(
    string? Gender = null,
    decimal WeightGram = 0,
    decimal CarapaceWidthMm = 0,
    decimal CarapaceLengthMm = 0,
    bool AutoAssign = true,
    Guid? TargetBoxId = null,
    string? Note = null,
    IReadOnlyList<string>? ImageUrls = null);

public record CreateCrabsBulkRequest(
    Guid CrabLotId,
    Guid? FarmingAreaId = null,
    Guid? FarmingRowId = null,
    string? CrabType = null,
    string? Condition = null,
    string? InitialCondition = null,
    DateTime? StockedAt = null,
    IReadOnlyList<CreateCrabBulkItem>? Items = null);

public record CreateCrabsBulkDto(
    IReadOnlyList<CrabDto> Crabs,
    int CreatedCount);

public record UpdateCrabRequest(
    string? MoltingStage,
    decimal? WeightGram,
    bool? IsAlive = null,
    DateTime? MoltedAt = null,
    IReadOnlyList<string>? ImageUrls = null,
    string? Condition = null,
    string? Notes = null,
    string? CrabType = null,
    string? Gender = null,
    decimal? CarapaceWidthMm = null,
    decimal? CarapaceLengthMm = null);
