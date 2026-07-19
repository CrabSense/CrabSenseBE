using CrabSenseBE.Domain.Common;

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
    public decimal? WeightAfterGram { get; set; }

    /// <summary>Kết quả: success | failed | incomplete.</summary>
    public string Result { get; set; } = "success";

    /// <summary>Nguồn ghi nhận: manual | ai.</summary>
    public string Source { get; set; } = "manual";

    public string? Notes { get; set; }

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
