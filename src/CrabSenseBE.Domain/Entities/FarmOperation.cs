using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Field operation log for Mobile (feeding, water change, notes, tasks…) — MOD-FARM.
/// Distinct from audit <see cref="OperationLog"/>.
/// </summary>
public class FarmOperation : BaseEntity
{
    /// <summary>feeding | waterChange | mineralAddition | cleaning | medication | inspection | note | task</summary>
    public string Type { get; set; } = "note";

    /// <summary>JSON array of box Guid strings.</summary>
    public string BoxIdsJson { get; set; } = "[]";

    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string Notes { get; set; } = string.Empty;

    /// <summary>JSON array of photo URLs.</summary>
    public string PhotoUrlsJson { get; set; } = "[]";

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>manual | auto — không xóa cột cũ, chỉ thêm.</summary>
    public string Source { get; set; } = "manual";
    public string? LocationLabel { get; set; }
}
