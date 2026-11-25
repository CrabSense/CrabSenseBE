using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

public class HarvestAssignment : BaseEntity
{
    public string AssignmentCode { get; set; } = string.Empty;

    public Guid FarmingAreaId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public Guid AssignedToUserId { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmissionNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewNotes { get; set; }

    public HarvestAssignmentStatus Status { get; set; }
        = HarvestAssignmentStatus.Assigned;

    public string? Notes { get; set; }

    public ICollection<HarvestAssignmentLine> Lines { get; set; }
        = new List<HarvestAssignmentLine>();

    public ICollection<HarvestVoucher> Vouchers { get; set; }
    = new List<HarvestVoucher>();
    public AppUser? AssignedBy { get; set; }
    public AppUser? AssignedTo { get; set; }
}

public class HarvestAssignmentLine : BaseEntity
{
    public Guid HarvestAssignmentId { get; set; }
    public Guid CrabId { get; set; }

    // Snapshot để giữ thông tin như lúc giao việc.
    public string? CrabCode { get; set; }
    public Guid? BoxId { get; set; }
    public string? BoxCode { get; set; }
    public HarvestAssignmentLineStatus Status { get; set; }
        = HarvestAssignmentLineStatus.Assigned;
    public DateTime? CheckedAt { get; set; }
    public Guid? CheckedByUserId { get; set; }
    public string? EligibilityReason { get; set; }
    public DateTime? HarvestedAt { get; set; }
    public string? Notes { get; set; }
    public HarvestAssignment? Assignment { get; set; }
    public Crab? Crab { get; set; }
    public HarvestLine? HarvestLine { get; set; }
    // Kết quả Staff đã chọn nhưng chưa nộp hoàn tất task.
    public HarvestAssignmentOutcome? Outcome { get; set; }
    // Thông tin thu hoạch được lưu tạm tại đây.
    // Chỉ tạo HarvestLine khi hoàn tất Assignment.
    public decimal? HarvestWeightGram { get; set; }
    public string? HarvestGrade { get; set; }
    public bool? HarvestIsSoftshell { get; set; }
    public string? HarvestConditionLabel { get; set; }
    public string? HarvestNotes { get; set; }
    public decimal? HarvestCarapaceLengthMm { get; set; }
    public decimal? HarvestCarapaceWidthMm { get; set; }

    // Thời điểm Staff nhập cho lần thu hoạch của cua này, lưu theo UTC.
    public DateTime? PendingHarvestedAt { get; set; }
    public bool PendingMoltConfirmed { get; set; } // để biết cua thu hoạch do lột hay do hết ngày quy định nhưng vẫn chưa lột
    public string HarvestPhotoUrlsJson { get; set; } = "[]";
}