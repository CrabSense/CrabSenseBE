namespace CrabSenseBE.Application.DTOs.Farm;

// --- CrabLot ---
public record CrabLotDto(
    Guid Id, string LotCode, DateTime ImportDate, int Quantity,
    decimal? AverageWeightGram, string? SupplierName, string? Notes);

/// <summary>
/// Create crab lot (lô nhập). Only LotCode + ImportDate (+ supplier/notes).
/// Quantity and AverageWeightGram are computed from crabs linked to this lot.
/// </summary>
public record CreateCrabLotRequest(
    string LotCode, DateTime ImportDate,
    string? SupplierName = null, string? Notes = null);

/// <summary>Update lot metadata only — quantity/avg weight are system-calculated.</summary>
public record UpdateCrabLotRequest(string? SupplierName, string? Notes);

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
/// (box must belong to row → area).
/// </summary>
public record AllocateCrabRequest(
    Guid FarmingAreaId,
    Guid FarmingRowId,
    Guid CrabId,
    Guid BoxId,
    string? Notes);

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
