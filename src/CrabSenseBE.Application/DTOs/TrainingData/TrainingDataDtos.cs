namespace CrabSenseBE.Application.DTOs.TrainingData;

public record CreateFeedingEventRequest(Guid CrabId, Guid? BoxId, DateTime? FedAt,
    string? FoodType, decimal? InitialFoodGram, string? Notes);

public record CreateObservationEventRequest(Guid CrabId, Guid? BoxId, Guid? FeedingEventId,
    DateTime? ObservedAt, int? DurationSeconds, string? ObservationType, string? Notes);

public record SubmitTrainingLabelRequest(Guid? FeedingEventId, Guid? ObservationEventId,
    bool? Ate, string? ConsumptionLevel, string? MovementLevel, string? FoodResponse,
    decimal? ActualWeightGram, string? Notes);

public record TrainingDataDto(Guid FeedingEventId, Guid? ObservationEventId, Guid CrabId,
    Guid? BoxId, decimal? InitialFoodGram, string? ConsumptionLevel, bool? Ate,
    string? MovementLevel, string? FoodResponse, Guid? VideoMediaId, DateTime FedAt,
    DateTime? ObservedAt);
