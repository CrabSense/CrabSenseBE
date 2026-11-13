using CrabSenseBE.Application.Common;

namespace CrabSenseBE.Application.DTOs.SystemPlatform;

/// <summary>Dates are UTC instants. Location filters use the camera's current assignment, not its location at capture time.</summary>
public record RecordingLibraryQuery(Guid? DeviceId = null, Guid? FarmingAreaId = null, Guid? FarmingRowId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Search = null, int Page = 1, int PageSize = 25);

public record RecordingLibraryItem(Guid Id, Guid DeviceId, string DeviceCode, string? CameraName,
    Guid? FarmingAreaId, string? AreaName, Guid? FarmingRowId, string? RowName,
    string FileName, string Category, string ContentType, long SizeBytes, DateTime CreatedAt,
    string Provider, bool IsShared, DateTime? StartedAt = null, DateTime? EndedAt = null,
    double? DurationSeconds = null, string? RecordingMode = null, string Source = "LegacyMedia");

public record RecordingLibraryPage(IReadOnlyList<RecordingLibraryItem> Items, int TotalCount, int Page, int PageSize,
    long TotalBytes, int CameraCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
