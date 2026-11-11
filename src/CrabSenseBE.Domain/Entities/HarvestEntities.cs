using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Phiếu thu hoạch cua lột — MOD-HARVEST</summary>
public class HarvestVoucher : BaseEntity
{
    public string VoucherCode { get; set; } = string.Empty;
    public DateTime HarvestDate { get; set; }
    public HarvestStatus Status { get; set; } = HarvestStatus.PendingApproval;
    public int TotalQuantity { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? FarmingAreaId { get; set; }
    public string? PerformedByName { get; set; }
    public string PhotoUrlsJson { get; set; } = "[]";
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? PerformedByUserId { get; set; }
    public DateTime? ExecutionCompletedAt { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public int ApprovalVersion { get; set; }
    public int ApprovedQuantity { get; set; }
    public int ExecutionShortfallCount { get; set; }
    public string? ExecutionShortfallReason { get; set; }
    public Guid? AdjustmentRequestedByUserId { get; set; }
    public DateTime? AdjustmentRequestedAt { get; set; }
    public string? AdjustmentReason { get; set; }
    public uint ConcurrencyVersion { get; set; }

    // Navigation
    public ICollection<HarvestLine> Lines { get; set; } = new List<HarvestLine>();
    public ICollection<HarvestApprovedLine> ApprovedLines { get; set; } = new List<HarvestApprovedLine>();
}

/// <summary>Chi tiết dòng thu hoạch</summary>
public class HarvestLine : BaseEntity
{
    public Guid HarvestVoucherId { get; set; }
    public Guid? CrabId { get; set; }
    public Guid? BoxId { get; set; }
    public decimal WeightGram { get; set; }
    public string? Grade { get; set; } // S, M, L
    public bool IsSoftshell { get; set; } = true;
    public string? Notes { get; set; }
    public string? ConditionLabel { get; set; }
    public string PhotoUrlsJson { get; set; } = "[]";
    public string? CrabCode { get; set; }
    public string? AreaName { get; set; }
    public string? RowName { get; set; }
    public string? BoxCode { get; set; }
    public string? LotCode { get; set; }
    /// <summary>passed | failed</summary>
    public string Result { get; set; } = "passed";
    
    // Navigation
    public HarvestVoucher? HarvestVoucher { get; set; }
    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}

/// <summary>
/// Snapshot bất biến của một dòng được FM duyệt. Mỗi lần điều chỉnh được duyệt
/// tạo version mới; các version cũ không bị ghi đè hoặc xóa.
/// </summary>
public class HarvestApprovedLine : BaseEntity
{
    public Guid HarvestVoucherId { get; set; }
    public int ApprovalVersion { get; set; }
    public Guid? SourceHarvestLineId { get; set; }
    public Guid? CrabId { get; set; }
    public Guid? BoxId { get; set; }
    public Guid FarmingAreaId { get; set; }
    public Guid ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; }
    public string? CrabCode { get; set; }
    public string? BoxCode { get; set; }
    public string? LotCode { get; set; }

    public HarvestVoucher? HarvestVoucher { get; set; }
    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}

