using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Phát hiện AI (detection result) — MOD-AI</summary>
public class AiDetection : BaseEntity
{
    public Guid? CrabId { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? BoxId { get; set; }
    public Guid? MediaId { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string DetectionType { get; set; } = string.Empty; // molting, health, grade
    public decimal Confidence { get; set; }
    public string? ResultJson { get; set; } // raw JSON from AI model
    public string? ImagePath { get; set; }
    public string Status { get; set; } = "pending"; // pending | completed | failed
    public DateTime DetectedAt { get; set; }

    // Navigation
    public Crab? Crab { get; set; }
    public Device? Device { get; set; }
    public ICollection<AiFeedback> Feedbacks { get; set; } = new List<AiFeedback>();
}

/// <summary>Phản hồi operator về kết quả AI</summary>
public class AiFeedback : BaseEntity
{
    public Guid AiDetectionId { get; set; }
    public Guid UserId { get; set; }
    public bool IsCorrect { get; set; }
    public string? CorrectLabel { get; set; }
    public string? Comment { get; set; }

    // Navigation
    public AiDetection? AiDetection { get; set; }
}

/// <summary>Khuyến nghị từ AI</summary>
public class AiRecommendation : BaseEntity
{
    public string Category { get; set; } = string.Empty; // feeding, water, harvest
    public string Recommendation { get; set; } = string.Empty;
    public decimal? Priority { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public bool IsActedUpon { get; set; } = false;
}

/// <summary>Kiểm tra chất lượng cua / manual inspection (Mobile + QA)</summary>
public class Inspection : BaseEntity
{
    public Guid? CrabId { get; set; }
    /// <summary>Box được kiểm tra (Mobile manual inspection).</summary>
    public Guid? BoxId { get; set; }
    public Guid InspectorId { get; set; }
    public string InspectionType { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public string? Notes { get; set; }
    public DateTime InspectedAt { get; set; }

    /// <summary>Mobile: molting status string.</summary>
    public string? MoltingStatus { get; set; }
    /// <summary>Mobile: health status string.</summary>
    public string? HealthStatus { get; set; }
    public decimal? WeightGram { get; set; }
    public Guid? RelatedMediaId { get; set; }
    public string? PhotoUrlsJson { get; set; }
    public string? OperatorName { get; set; }
    public bool? AiAgreement { get; set; }

    // Navigation
    public Crab? Crab { get; set; }
    public Box? Box { get; set; }
}
