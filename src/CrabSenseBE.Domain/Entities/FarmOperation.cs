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

    /// <summary>
    /// JSON array of crab Guid strings — con cua được ghi nhận trong phiếu này.
    /// Tham chiếu mềm (không FK), cùng kiểu với <see cref="BoxIdsJson"/>.
    /// </summary>
    public string CrabIdsJson { get; set; } = "[]";

    /// <summary>Mức ăn quan sát được: many | little | none (null = không ghi).</summary>
    public string? Appetite { get; set; }

    /// <summary>Loại thức ăn cho phiếu cho ăn (null với phiếu khác).</summary>
    public string? FoodType { get; set; }

    /// <summary>
    /// Tình trạng đánh dấu khi cho ăn: normal | premolt | attention | weak.
    /// Ghi vào FarmOperations; nếu có CrabIds thì cập nhật luôn Crabs.Condition.
    /// </summary>
    public string? Condition { get; set; }

    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }

    /// <summary>Lượng thức ăn cua đã ăn (cùng đơn vị với <see cref="Quantity"/>). Null = chưa đo.</summary>
    public decimal? EatenQuantity { get; set; }

    /// <summary>Điểm vận động chuẩn hoá 0–100 trước khi cho ăn (AI camera / thủ công).</summary>
    public int? ActivityBefore { get; set; }

    /// <summary>Điểm vận động chuẩn hoá 0–100 sau khi cho ăn.</summary>
    public int? ActivityAfter { get; set; }

    /// <summary>Thời gian cua ăn (phút), nếu quan sát được.</summary>
    public int? FeedingDurationMinutes { get; set; }

    /// <summary>Camera liên quan (mã hoặc Guid) đã ghi nhận lần cho ăn.</summary>
    public string? CameraId { get; set; }

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
