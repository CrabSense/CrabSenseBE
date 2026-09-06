using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>
/// Một lần phân tích hóa học (thuốc thử + camera + AI màu).
/// Không phải mẫu realtime từ cảm biến TDS/nhiệt/mặn.
/// </summary>
public class WaterAnalysisRun : BaseEntity
{
    public Guid FarmingAreaId { get; set; }
    /// <summary>running | completed | failed</summary>
    public string Status { get; set; } = "running";
    /// <summary>1 lấy mẫu … 6 lưu kết quả</summary>
    public int CurrentStep { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastStepAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public decimal? Ph { get; set; }
    public decimal? Nh3 { get; set; }
    public decimal? No2 { get; set; }
    public decimal? No3 { get; set; }

    public string? ImageUrl { get; set; }
    public string? Error { get; set; }
    public string Source { get; set; } = "colorimetric-ai";

    public FarmingArea? FarmingArea { get; set; }
}
