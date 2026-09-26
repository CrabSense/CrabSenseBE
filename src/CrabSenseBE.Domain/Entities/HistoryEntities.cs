using CrabSenseBE.Domain.Common;
using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Lịch sử phân bổ cua vào box (di chuyển / thả nuôi).
/// </summary>
public class CrabBoxAllocation : BaseEntity
{
    public Guid CrabId { get; set; }
    public Guid BoxId { get; set; }

    /// <summary>Thời điểm bắt đầu ở box này.</summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>Null = đang ở box hiện tại; có giá trị = đã chuyển đi.</summary>
    public DateTime? EndTime { get; set; }

    public string? Notes { get; set; }

    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}

/// <summary>
/// Lịch sử lột xác của từng con cua.
/// </summary>
public class MoltingRecord : BaseEntity
{
    public Guid CrabId { get; set; }
    public Guid? BoxId { get; set; }

    public DateTime MoltTime { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal? WeightBeforeGram { get; set; }
    public decimal? WeightAfterGram { get; set; }
    public decimal? ShellWidthBeforeMm { get; set; }
    public decimal? ShellLengthBeforeMm { get; set; }
    public decimal? ShellWidthAfterMm { get; set; }
    public decimal? ShellLengthAfterMm { get; set; }
    public string? CameraId { get; set; }

    /// <summary>Kết quả: success | monitoring | abnormal | failed.</summary>
    public string Result { get; set; } = "success";

    /// <summary>Nguồn ghi nhận: manual | ai.</summary>
    public string Source { get; set; } = "manual";

    public string? Notes { get; set; }

    /// <summary>JSON array URL ảnh lột xác trên Drive.</summary>
    public string PhotoUrlsJson { get; set; } = "[]";

    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}

/// <summary>
/// Lịch sử đổi trạng thái nuôi của hộp (audit trail).
/// </summary>
public class BoxStatusHistory : BaseEntity
{
    public Guid BoxId { get; set; }
    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public bool? OldIsOccupied { get; set; }
    public bool NewIsOccupied { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }
    public Guid? ChangedByUserId { get; set; }

    public Box? Box { get; set; }
    public AppUser? ChangedByUser { get; set; }
}

/// <summary>Lịch sử trạng thái cua — không lưu dồn trên bảng Crabs.</summary>
public class CrabStatusHistory : BaseEntity
{
    public Guid CrabId { get; set; }
    public CrabCondition? OldCondition { get; set; }
    public CrabCondition NewCondition { get; set; }
    public CrabStatus? OldStatus { get; set; }
    public CrabStatus NewStatus { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "system";
    public string? Reason { get; set; }
    public Guid? ChangedByUserId { get; set; }

    public Crab? Crab { get; set; }
    public AppUser? ChangedByUser { get; set; }
}

/// <summary>Lịch sử cân nặng cua.</summary>
public class CrabWeightHistory : BaseEntity
{
    public Guid CrabId { get; set; }
    public decimal WeightGram { get; set; }
    public decimal? CarapaceWidthMm { get; set; }
    public decimal? CarapaceLengthMm { get; set; }
    public DateTime MeasuredAt { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "manual";
    public string? Notes { get; set; }
    public string? RecordedByName { get; set; }
    public string PhotoUrlsJson { get; set; } = "[]";

    public Crab? Crab { get; set; }
}

/// <summary>Kết quả AI theo thời điểm — không nhập tay trên form cua.</summary>
public class CrabAiAnalysis : BaseEntity
{
    public Guid CrabId { get; set; }
    public Guid? BoxId { get; set; }
    public string Prediction { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? ActivityLevel { get; set; }
    public string? AnomalyNote { get; set; }
    public string? MediaUrl { get; set; }
    public string? ModelVersion { get; set; }
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;

    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}

/// <summary>Mốc thu hoạch của từng con cua (timeline).</summary>
public class CrabHarvestHistory : BaseEntity
{
    public Guid CrabId { get; set; }
    public Guid? HarvestLineId { get; set; }
    public DateTime HarvestedAt { get; set; } = DateTime.UtcNow;
    public decimal? WeightGram { get; set; }
    public string? Grade { get; set; }
    public string? Notes { get; set; }

    public Crab? Crab { get; set; }
    public HarvestLine? HarvestLine { get; set; }
}
