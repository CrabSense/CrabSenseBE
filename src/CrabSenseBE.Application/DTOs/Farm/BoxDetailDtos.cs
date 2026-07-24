namespace CrabSenseBE.Application.DTOs.Farm;

public record BoxLocationDto(double Latitude, double Longitude, string? Label);

/// <summary>
/// Enriched box detail for Mobile Box Detail screen (compatible with lean list BoxDto fields).
/// </summary>
public record BoxDetailDto(
    Guid Id,
    Guid FarmingRowId,
    Guid FarmingAreaId,
    string? RowName,
    string? AreaName,
    string Code,
    string? Status,
    bool IsOccupied,
    string QrCode,
    Guid FarmId,
    Guid? PondId,
    BoxLocationDto Location,
    int CurrentCrabCount,
    int Capacity,
    string Species,
    decimal AverageWeight,
    DateTime CreatedAt,
    DateTime? LastVideoAt);

/// <summary>Mobile-friendly crab row under a box.</summary>
public record BoxCrabItemDto(
    Guid Id,
    Guid BoxId,
    string Species,
    decimal Weight,
    string MoltingStatus,
    string HealthStatus,
    string Source,
    DateTime AddedAt,
    string AddedBy,
    // Backend aliases
    decimal? WeightGram,
    string? MoltingStage,
    string? Tag);

/// <summary>Mobile video list item (mapped from MediaAsset).</summary>
public record BoxVideoItemDto(
    Guid Id,
    Guid BoxId,
    string LocalPath,
    int DurationSeconds,
    long? FileSizeBytes,
    string Status,
    DateTime CapturedAt,
    string CapturedBy,
    DateTime? UploadedAt,
    string? AiDetectionId,
    int RetryCount);
