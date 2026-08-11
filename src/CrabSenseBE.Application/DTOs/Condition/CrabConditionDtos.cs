namespace CrabSenseBE.Application.DTOs.Condition;

/// <summary>
/// Evaluate Kn (Relative Condition Factor) — port from crab_condition/crab_condition.py.
/// Provide weightG + at least one of carapaceWidthCm / carapaceLengthCm.
/// </summary>
public record EvaluateCrabConditionRequest(
    decimal WeightG,
    decimal? CarapaceWidthCm = null,
    decimal? CarapaceLengthCm = null,
    string CrabType = "unknown",
    string? BoxCode = null,
    Guid? BoxId = null,
    Guid? CrabId = null,
    /// <summary>cw = ras_hcm_raw (default) | cl = KN_METHOD CL formula</summary>
    string Dimension = "auto");

public record EvaluateCrabConditionBatchRequest(
    IReadOnlyList<EvaluateCrabConditionRequest> Items);

/// <summary>Evaluate + persist as Inspection (type=condition) for mobile history.</summary>
public record SubmitCrabConditionRequest(
    Guid BoxId,
    decimal WeightG,
    decimal? CarapaceWidthCm = null,
    decimal? CarapaceLengthCm = null,
    string CrabType = "unknown",
    Guid? CrabId = null,
    string Dimension = "auto",
    string? Notes = null,
    IReadOnlyList<string>? PhotoUrls = null,
    Guid? OperatorId = null,
    string? OperatorName = null,
    bool? IsSoftShell = null,
    bool? HasGoodReflex = null,
    bool? HasDoubleLine = null,
    bool? IsMolted = null,
    bool UpdateCrabRecord = true);

public record CrabConditionDto(
    Guid? Id,
    Guid? BoxId,
    string? BoxCode,
    Guid? CrabId,
    decimal WeightObservedG,
    decimal? CarapaceWidthCm,
    decimal? CarapaceLengthCm,
    decimal WeightEstimatedG,
    decimal Kn,
    string Status,
    string Recommendation,
    bool AlertHarvest,
    string ProfileUsed,
    string DimensionUsed,
    decimal? MeatEstimatedG,
    string? MoltingStatusHint,
    DateTime? RecordedAt);

public record DailyCheckRequest(
    Guid? BoxId = null,
    Guid? CrabId = null,
    string CrabType = "unknown",
    decimal? WeightG = null,
    decimal? CarapaceWidthCm = null,
    decimal? CarapaceLengthCm = null,
    decimal? WeightInitialG = null,
    decimal? CarapaceLengthInitialCm = null,
    int DaysInCulture = 0,
    decimal? WaterTempC = null);

public record DailyCheckDto(
    decimal? Kn,
    string? Status,
    bool AlertHarvest,
    string Recommendation,
    decimal? WeightPredictedDwg,
    decimal? FeedTomorrowG,
    string FeedTime,
    string FeedType);
