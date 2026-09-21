using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;

namespace CrabSenseBE.Application.DTOs.Ops;

public record FarmOperationDto(
    Guid Id,
    string Type,
    IReadOnlyList<string> BoxIds,
    decimal? Quantity,
    string? Unit,
    string Notes,
    IReadOnlyList<string> PhotoUrls,
    DateTime Timestamp,
    Guid OperatorId,
    string OperatorName,
    string Source = "manual",
    string? LocationLabel = null,
    IReadOnlyList<string>? CrabIds = null,
    string? Appetite = null,
    string? FoodType = null,
    string? Condition = null);

public record FeedingHistoryDayDto(
    DateOnly Date,
    int Many,
    int Little,
    int None);

public record CreateFarmOperationRequest(
    string Type,
    IReadOnlyList<string>? BoxIds = null,
    string? Notes = null,
    decimal? Quantity = null,
    string? Unit = null,
    IReadOnlyList<string>? PhotoUrls = null,
    DateTime? Timestamp = null,
    Guid? OperatorId = null,
    string? OperatorName = null,
    string? Source = null,
    string? LocationLabel = null,
    IReadOnlyList<string>? CrabIds = null,
    string? Appetite = null,
    string? FoodType = null,
    string? Condition = null);

public record UpdateFarmOperationRequest(
    string? Type = null,
    IReadOnlyList<string>? BoxIds = null,
    string? Notes = null,
    decimal? Quantity = null,
    string? Unit = null,
    IReadOnlyList<string>? PhotoUrls = null,
    IReadOnlyList<string>? CrabIds = null,
    string? Appetite = null,
    string? FoodType = null,
    string? Condition = null);

public record ManualInspectionDto(
    Guid Id,
    Guid BoxId,
    Guid? RelatedVideoId,
    string MoltingStatus,
    string HealthStatus,
    decimal Weight,
    string Notes,
    IReadOnlyList<string> PhotoUrls,
    DateTime Timestamp,
    Guid OperatorId,
    string OperatorName,
    bool? AiAgreement);

public record SubmitManualInspectionRequest(
    Guid BoxId,
    string? Id = null,
    Guid? RelatedVideoId = null,
    string? MoltingStatus = null,
    string? HealthStatus = null,
    decimal? Weight = null,
    string? Notes = null,
    IReadOnlyList<string>? PhotoUrls = null,
    DateTime? Timestamp = null,
    Guid? OperatorId = null,
    string? OperatorName = null,
    bool? AiAgreement = null);

/// <summary>
/// Mobile-friendly add crab under a box (auto-picks CrabLot when omitted).
/// Owner fields (gender/type/condition/notes/carapace) mirror desktop CreateCrabRequest —
/// they were previously absent, so mobile input was silently dropped by the binder.
/// </summary>
public record MobileAddCrabRequest(
    Guid? CrabLotId = null,
    string? Tag = null,
    decimal? Weight = null,
    decimal? WeightGram = null,
    string? MoltingStatus = null,
    string? MoltingStage = null,
    string? Species = null,
    IReadOnlyList<string>? ImageUrls = null,
    string? Gender = null,
    string? CrabType = null,
    string? Condition = null,
    string? Notes = null,
    decimal? CarapaceLengthMm = null,
    decimal? CarapaceWidthMm = null);

public record AiAnalyzeRequest(Guid? MediaId = null, Guid? VideoId = null, Guid? BoxId = null);

public record AiDetectionDto(
    Guid Id,
    Guid? BoxId,
    Guid? MediaId,
    string DetectionType,
    decimal Confidence,
    string Status,
    string? ResultJson,
    DateTime DetectedAt,
    string ModelVersion,
    Guid? DeviceId = null,
    string? DeviceCode = null,
    string? ImagePath = null,
    string? BoxCode = null,
    Guid? CrabId = null,
    string? CrabTag = null,
    Guid? FarmingRowId = null,
    string? RowName = null,
    Guid? FarmingAreaId = null);

public record AiFeedbackRequest(
    Guid? AiDetectionId = null,
    Guid? DetectionId = null,
    bool IsCorrect = true,
    string? CorrectLabel = null,
    string? Comment = null,
    bool? AiAgreement = null);
