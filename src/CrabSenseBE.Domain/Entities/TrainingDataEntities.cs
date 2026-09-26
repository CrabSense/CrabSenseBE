using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

public class FeedingEvent : BaseEntity
{
    public Guid CrabId { get; set; }
    public Guid? BoxId { get; set; }
    public DateTime FedAt { get; set; } = DateTime.UtcNow;
    public string? FoodType { get; set; }
    public decimal? InitialFoodGram { get; set; }
    public decimal? EstimatedConsumedPercent { get; set; }
    public decimal? EstimatedConsumedGram { get; set; }
    public string? ConsumptionLevel { get; set; }
    public decimal? Confidence { get; set; }
    public string MeasurementStatus { get; set; } = "manual";
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }
}

public class ObservationEvent : BaseEntity
{
    public Guid CrabId { get; set; }
    public Guid? BoxId { get; set; }
    public Guid? FeedingEventId { get; set; }
    public DateTime ObservedAt { get; set; } = DateTime.UtcNow;
    public int? DurationSeconds { get; set; }
    public string ObservationType { get; set; } = "AFTER_FEEDING";
    public Guid? VideoMediaId { get; set; }
    public string ProcessingStatus { get; set; } = "uploaded";
    public string? Notes { get; set; }
}

public class TrainingLabel : BaseEntity
{
    public Guid? FeedingEventId { get; set; }
    public Guid? ObservationEventId { get; set; }
    public bool? Ate { get; set; }
    public string? ConsumptionLevel { get; set; }
    public string? MovementLevel { get; set; }
    public string? FoodResponse { get; set; }
    public decimal? ActualWeightGram { get; set; }
    public string LabelSource { get; set; } = "operator";
    public Guid? LabeledBy { get; set; }
    public DateTime LabeledAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
