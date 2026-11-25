namespace CrabSenseBE.Application.DTOs.Harvest;

using CrabSenseBE.Domain.Enums;
// ─────────────────────────────────────────────────────────────────────────────
// Harvest Voucher
// ─────────────────────────────────────────────────────────────────────────────

public record HarvestVoucherDto(
    Guid Id,
    string VoucherCode,
    DateTime HarvestDate,
    string Status,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    string? Notes,
    Guid CreatedBy,
    DateTime CreatedAt,
    Guid? FarmingAreaId = null,
    string? PerformedByName = null,
    string? AreaName = null,
    int PassedCount = 0,
    int FailedCount = 0,
    decimal AverageWeightGram = 0,
    IReadOnlyList<string>? PhotoUrls = null,
    IReadOnlyCollection<HarvestLineDto>? Lines = null,
    Guid? ApprovedByUserId = null,
    DateTime? ApprovedAt = null,
    string? RejectionReason = null,
    Guid? PerformedByUserId = null,
    DateTime? ExecutionCompletedAt = null,
    Guid? ConfirmedByUserId = null,
    DateTime? ConfirmedAt = null,
    int ApprovalVersion = 0,
    int ApprovedQuantity = 0,
    int ExecutionShortfallCount = 0,
    string? ExecutionShortfallReason = null,
    Guid? AdjustmentRequestedByUserId = null,
    DateTime? AdjustmentRequestedAt = null,
    string? AdjustmentReason = null
);

public record HarvestVoucherDetailDto(
    Guid Id,
    string VoucherCode,
    DateTime HarvestDate,
    string Status,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    string? Notes,
    Guid CreatedBy,
    DateTime CreatedAt,
    IReadOnlyCollection<HarvestLineDto> Lines,
    Guid? FarmingAreaId = null,
    string? PerformedByName = null,
    string? AreaName = null,
    int PassedCount = 0,
    int FailedCount = 0,
    decimal AverageWeightGram = 0,
    IReadOnlyList<string>? PhotoUrls = null,
    Guid? ApprovedByUserId = null,
    DateTime? ApprovedAt = null,
    string? RejectionReason = null,
    Guid? PerformedByUserId = null,
    DateTime? ExecutionCompletedAt = null,
    Guid? ConfirmedByUserId = null,
    DateTime? ConfirmedAt = null,
    int ApprovalVersion = 0,
    int ApprovedQuantity = 0,
    int ExecutionShortfallCount = 0,
    string? ExecutionShortfallReason = null,
    IReadOnlyCollection<HarvestApprovedLineDto>? ApprovedLines = null,
    Guid? AdjustmentRequestedByUserId = null,
    DateTime? AdjustmentRequestedAt = null,
    string? AdjustmentReason = null
);

public record HarvestLineDto(
    Guid Id,
    Guid? CrabId,
    Guid? BoxId,
    string? BoxCode,
    decimal WeightGram,
    string? Grade,
    bool IsSoftshell,
    string? Notes,
    string? CrabCode = null,
    string? ConditionLabel = null,
    IReadOnlyList<string>? PhotoUrls = null,
    string? AreaName = null,
    string? RowName = null,
    string? LotCode = null,
    string? Result = null,
    decimal? CarapaceLengthMm = null,
    decimal? CarapaceWidthMm = null,
    DateTime? HarvestedAt = null
);

public record CreateHarvestVoucherRequest(
    DateTime HarvestDate,
    string? Notes,
    IEnumerable<HarvestLineRequest> Lines,
    Guid? FarmingAreaId = null,
    string? PerformedByName = null,
    IEnumerable<string>? PhotoUrls = null,
    string? Status = null
);

public record HarvestLineRequest(
    Guid? CrabId,
    decimal WeightGram,
    string? Grade,
    bool IsSoftshell,
    string? Notes = null,
    string? ConditionLabel = null,
    IEnumerable<string>? PhotoUrls = null,
    string? Result = null
);

public record HarvestOverviewDto(
    int HarvestableCount,
    int HarvestedToday,
    int WaitingSale,
    decimal TotalHarvestWeightKg,
    int SoftshellWaiting = 0
);

public record UpdateHarvestStatusRequest(
    string Status
);

public record ApproveHarvestRequest(string? Notes = null);

public record RejectHarvestRequest(string Reason);

public record ExecuteHarvestRequest(
    IEnumerable<HarvestLineRequest> Lines,
    string? Notes = null,
    IEnumerable<string>? PhotoUrls = null,
    string? ShortfallReason = null);

public record ConfirmHarvestRequest(string? Notes = null);

public record RequestHarvestAdjustmentRequest(
    IEnumerable<HarvestLineRequest> Lines,
    string Reason);

public record HarvestApprovedLineDto(
    Guid Id,
    int ApprovalVersion,
    Guid? CrabId,
    Guid? BoxId,
    Guid FarmingAreaId,
    Guid ApprovedByUserId,
    DateTime ApprovedAt,
    string? CrabCode,
    string? BoxCode,
    string? LotCode);

// ─────────────────────────────────────────────────────────────────────────────
// Harvest Statistics
// ─────────────────────────────────────────────────────────────────────────────

public record HarvestStatisticsDto(
    DateTime From,
    DateTime To,
    string Period,
    int VoucherCount,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate,
    IReadOnlyCollection<HarvestPeriodItemDto> Items
);

public record HarvestPeriodItemDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int VoucherCount,
    int TotalQuantity,
    decimal TotalWeightKg,
    int SoftshellQuantity,
    decimal SoftshellRate
);


// ─────────────────────────────────────────────────────────────────────────────
// QR / Traceability
// ─────────────────────────────────────────────────────────────────────────────

public record QrCodeDto(
    Guid Id,
    string Code,
    Guid? FrozenLotId,
    int ScanCount,
    DateTime? ExpiresAt,
    string ApprovalStatus = "Approved",
    Guid? ProposedByUserId = null,
    Guid? ApprovedByUserId = null,
    DateTime? ApprovedAt = null,
    string? RejectionReason = null,
    string? DecisionReason = null
);

public record ApproveQrAssignmentRequest(string Reason);
public record RejectQrAssignmentRequest(string Reason);

public record GenerateQrRequest(
    Guid? FrozenLotId,
    Guid? HarvestVoucherId,
    DateTime? ExpiresAt
);

public record TraceabilityPublicDto(
    string Code,
    string? FrozenLotCode,
    string? HarvestVoucherCode,
    DateTime? FrozenDate,
    DateTime? HarvestDate,
    string? Grade,
    IReadOnlyList<TraceabilityFrozenLotDto>? FrozenLots = null,
    IReadOnlyList<string>? Grades = null
);

public record TraceabilityFrozenLotDto(string LotCode, DateTime? FrozenDate, string? Grade);

// ─────────────────────────────────────────────────────────────────────────────
// Box ↔ Harvest link
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Thông tin box tham chiếu trong phiếu thu hoạch.</summary>
public record HarvestBoxDto(
    Guid BoxId,
    string BoxCode,
    int CrabCount,
    decimal TotalWeightGram
);

/// <summary>Thông tin phiếu thu hoạch tham chiếu đến box.</summary>
public record BoxHarvestVoucherDto(
    Guid VoucherId,
    string VoucherCode,
    DateTime HarvestDate,
    string Status,
    int CrabCount,
    decimal TotalWeightGram
);

public record HarvestAssignmentDto(
    Guid Id,
    string AssignmentCode,
    Guid FarmingAreaId,
    Guid AssignedToUserId,
    string Status,
    int LineCount);
public record CreateHarvestAssignmentRequest(
    Guid FarmingAreaId,
    Guid AssignedToUserId,
    IReadOnlyCollection<Guid> CrabIds,
    string? Notes = null);

public record HarvestAssignmentResultRequest(
    Guid AssignmentLineId,
    HarvestAssignmentOutcome Outcome,
    decimal? WeightGram = null,
    string? Grade = null,
    bool IsSoftshell = true,
    string? Reason = null,
    string? Notes = null,
    string? ConditionLabel = null,
    decimal? CarapaceLengthMm = null, // Chiều dài mai
    decimal? CarapaceWidthMm = null, // Chiều rộng mai
    DateTimeOffset? HarvestedAt = null, // Thời điểm thu hoạch
    IReadOnlyList<string>? PhotoUrls = null,
    bool MoltConfirmed = false); // để biết cua thu hoạch do lột hay do hết ngày quy định nhưng vẫn chưa lột

public record SubmitHarvestAssignmentResultsRequest(
    IReadOnlyCollection<HarvestAssignmentResultRequest> Lines,
    string? Notes = null);

public record HarvestAssignmentResultDto(
    Guid AssignmentId,
    string AssignmentCode,
    string AssignmentStatus,
    Guid? VoucherId,
    string? VoucherCode,
    int HarvestedCount,
    int DeferredCount);

public record HarvestAssignmentListItemDto(
Guid Id,
string AssignmentCode,
Guid FarmingAreaId,
Guid AssignedToUserId,
string Status,
DateTime CreatedAt,
DateTime? StartedAt,
DateTime? CompletedAt,
int LineCount,
int HarvestedCount,
int DeferredCount,
int VoucherCount);

public record HarvestAssignmentLineDetailDto(
    Guid Id,
    Guid CrabId,
    string? CrabCode,
    Guid? BoxId,
    string? BoxCode,
    string Status,
    DateTime? CheckedAt,
    Guid? CheckedByUserId,
    string? EligibilityReason,
    DateTime? HarvestedAt,
    string? Notes,
    Guid? HarvestLineId,
    decimal? WeightGram,
    string? Grade,
    HarvestAssignmentOutcome? Outcome,
    decimal? PendingWeightGram,
    string? PendingGrade,
    bool? PendingIsSoftshell,
    string? PendingConditionLabel,
    string? PendingHarvestNotes,
    decimal? PendingCarapaceLengthMm,
    decimal? PendingCarapaceWidthMm,
    DateTime? PendingHarvestedAt,
    IReadOnlyList<string>? PendingPhotoUrls,
    bool? PendingMoltConfirmed = null);

public record HarvestAssignmentVoucherDto(
    Guid Id,
    string VoucherCode,
    string Status,
    DateTime HarvestDate,
    int TotalQuantity,
    decimal TotalWeightKg);

public record HarvestAssignmentDetailDto(
    Guid Id,
    string AssignmentCode,
    Guid FarmingAreaId,
    Guid AssignedByUserId,
    Guid AssignedToUserId,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Notes,
    IReadOnlyCollection<HarvestAssignmentLineDetailDto> Lines,
    IReadOnlyCollection<HarvestAssignmentVoucherDto> Vouchers,
    DateTime? SubmittedAt = null,
    string? SubmissionNotes = null,
    DateTime? ReviewedAt = null,
    Guid? ReviewedByUserId = null,
    string? ReviewNotes = null);

public record HarvestAssignmentStaffDto(
    Guid Id,
    string FullName,
    string? UserCode);
public record CompleteHarvestAssignmentRequest(string? VoucherNotes = null);

public record ReviewHarvestAssignmentRequest(string? Notes = null);
