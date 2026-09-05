namespace CrabSenseBE.Application.DTOs.Farm;

// --- CrabLot ---
public record CrabLotDto(
    Guid Id,
    string LotCode,
    string Name,
    DateTime ImportDate,
    int Quantity,
    int PlacedCount,
    string? SupplierName,
    decimal? TotalWeightKg,
    decimal? AverageWeightGram,
    decimal? WeightMinGram,
    decimal? WeightMaxGram,
    decimal? UnitPriceVndPerKg,
    decimal? CrabCostVnd,
    decimal? ShippingCostVnd,
    decimal? OtherCostVnd,
    decimal? TotalCostVnd,
    string Condition,
    int DeadOnArrival,
    string? Notes,
    string Status);

public record NextCrabLotCodeDto(string Code);

/// <summary>
/// Nhập lô. Required: name + quantity.
/// lotCode omit → LOT-yyyyMMdd-001. average / tiền tự tính từ tổng kg + số lượng + giá.
/// </summary>
public record CreateCrabLotRequest(
    string? Name = null,
    DateTime? ImportDate = null,
    int Quantity = 0,
    string? LotCode = null,
    string? SupplierName = null,
    decimal? TotalWeightKg = null,
    decimal? WeightMinGram = null,
    decimal? WeightMaxGram = null,
    decimal? UnitPriceVndPerKg = null,
    decimal? ShippingCostVnd = null,
    decimal? OtherCostVnd = null,
    string? Condition = null,
    int DeadOnArrival = 0,
    string? Notes = null);

public record UpdateCrabLotRequest(
    string? Name = null,
    DateTime? ImportDate = null,
    int? Quantity = null,
    string? SupplierName = null,
    decimal? TotalWeightKg = null,
    decimal? WeightMinGram = null,
    decimal? WeightMaxGram = null,
    decimal? UnitPriceVndPerKg = null,
    decimal? ShippingCostVnd = null,
    decimal? OtherCostVnd = null,
    string? Condition = null,
    int? DeadOnArrival = null,
    string? Notes = null,
    string? Status = null);

// --- CropBatch ---
public record CropBatchDto(
    Guid Id, string BatchCode, DateTime StartDate, DateTime? EndDate, string? Status, string? Notes);

/// <summary>Create crop batch. Required: BatchCode, StartDate.</summary>
public record CreateCropBatchRequest(string BatchCode, DateTime StartDate, string? Notes);
public record UpdateCropBatchRequest(DateTime? EndDate, string? Status, string? Notes);

// --- Allocation ---
public record CrabBoxAllocationDto(
    Guid Id, Guid CrabId, Guid BoxId, DateTime StartTime, DateTime? EndTime, string? Notes);

/// <summary>
/// Allocate crab into a box. Required: FarmingAreaId + FarmingRowId + CrabId + BoxId
/// (box must belong to row → area). Area/Row may be omitted when BoxId is set (auto-filled).
/// </summary>
public record AllocateCrabRequest(
    Guid CrabId,
    Guid BoxId,
    Guid? FarmingAreaId = null,
    Guid? FarmingRowId = null,
    string? Notes = null,
    // Mobile transfer aliases
    Guid? SourceBoxId = null,
    Guid? DestinationBoxId = null);

/// <summary>Mobile-friendly transfer: crabId + destinationBoxId (+ optional sourceBoxId).</summary>
public record MobileTransferCrabRequest(
    Guid CrabId,
    Guid DestinationBoxId,
    Guid? SourceBoxId = null,
    string? Notes = null);

/// <summary>Sửa bản ghi allocation (ghi chú / thời gian) — không đổi crab/box.</summary>
public record UpdateAllocationRequest(
    DateTime? StartTime,
    DateTime? EndTime,
    string? Notes);

// --- Molting history ---
public record MoltingRecordDto(
    Guid Id, Guid CrabId, Guid? BoxId, DateTime MoltTime,
    decimal? WeightAfterGram, string Result, string Source, string? Notes);

public record CreateMoltingRecordRequest(
    Guid CrabId, Guid? BoxId, DateTime? MoltTime,
    decimal? WeightAfterGram, string? Result, string? Source, string? Notes);

/// <summary>Sửa lần ghi lột xác (lỡ nhập sai).</summary>
public record UpdateMoltingRecordRequest(
    Guid? BoxId,
    DateTime? MoltTime,
    decimal? WeightAfterGram,
    string? Result,
    string? Source,
    string? Notes);

/// <summary>One box status change event.</summary>
public record BoxStatusHistoryDto(
    Guid Id,
    Guid BoxId,
    string? OldStatus,
    string NewStatus,
    bool? OldIsOccupied,
    bool NewIsOccupied,
    DateTime ChangedAt,
    string? Reason);

/// <summary>Sửa dòng audit trạng thái hộp (lỡ ghi sai).</summary>
public record UpdateBoxStatusHistoryRequest(
    string? OldStatus,
    string? NewStatus,
    bool? OldIsOccupied,
    bool? NewIsOccupied,
    DateTime? ChangedAt,
    string? Reason);

/// <summary>
/// Live farming snapshot of a box: status + current crab + open allocation + molt counts.
/// </summary>
public record BoxFarmingStatusDto(
    Guid BoxId,
    string Code,
    string? Status,
    bool IsOccupied,
    Guid FarmingRowId,
    Guid FarmingAreaId,
    string? RowName,
    string? AreaName,
    Guid? CurrentCrabId,
    string? CurrentCrabTag,
    string? CurrentMoltingStage,
    decimal? CurrentWeightGram,
    bool? CurrentCrabAlive,
    Guid? OpenAllocationId,
    DateTime? AllocationStartedAt,
    int AllocationCount,
    int MoltingCount,
    DateTime? LastMoltAt,
    string? LastMoltResult);

/// <summary>Combined farming timeline entry for a box (allocation | molting | status).</summary>
public record BoxFarmingEventDto(
    string EventType,
    DateTime At,
    string Summary,
    Guid? CrabId,
    Guid? RelatedId);
