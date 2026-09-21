namespace CrabSenseBE.Application.DTOs.Ops;

public record ScheduledTaskDto(
    Guid Id,
    Guid OwnerId,
    Guid? FarmingAreaId,
    string Title,
    string? Description,
    string RecurrenceType,
    IReadOnlyList<int> DaysOfWeek,
    DateTime StartDate,
    DateTime? EndDate,
    int ReminderMinuteOfDay,
    bool IsEnabled,
    DateTime? NextRunAt);

public record CreateScheduledTaskRequest(
    string Title,
    string? Description = null,
    Guid? FarmingAreaId = null,
    string RecurrenceType = "once",
    IReadOnlyList<int>? DaysOfWeek = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    int ReminderMinuteOfDay = 420,
    bool IsEnabled = true);

public record UpdateScheduledTaskRequest(
    string? Title = null,
    string? Description = null,
    Guid? FarmingAreaId = null,
    string? RecurrenceType = null,
    IReadOnlyList<int>? DaysOfWeek = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    int? ReminderMinuteOfDay = null,
    bool? IsEnabled = null);
