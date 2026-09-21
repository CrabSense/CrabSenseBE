using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public sealed class ScheduledTaskService : IScheduledTaskService
{
    private readonly IUnitOfWork _uow;

    public ScheduledTaskService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<IEnumerable<ScheduledTaskDto>>> ListAsync(
        Guid ownerId, Guid? farmingAreaId, CancellationToken ct = default)
    {
        var tasks = await _uow.ScheduledFarmTasks.FindAsync(
            task => task.OwnerId == ownerId &&
                    (!farmingAreaId.HasValue ||
                     task.FarmingAreaId == farmingAreaId ||
                     task.FarmingAreaId == null),
            ct);
        return ApiResponse<IEnumerable<ScheduledTaskDto>>.Ok(tasks
            .OrderBy(task => task.ReminderMinuteOfDay)
            .Select(Map));
    }

    public async Task<ApiResponse<IEnumerable<ScheduledTaskDto>>> TodayAsync(
        Guid ownerId, Guid? farmingAreaId, CancellationToken ct = default)
    {
        var tasks = await _uow.ScheduledFarmTasks.FindAsync(
            task => task.OwnerId == ownerId &&
                    task.IsEnabled &&
                    (!farmingAreaId.HasValue ||
                     task.FarmingAreaId == farmingAreaId ||
                     task.FarmingAreaId == null),
            ct);
        var today = DateTime.UtcNow.Date;
        return ApiResponse<IEnumerable<ScheduledTaskDto>>.Ok(
            tasks
                .Where(task => IsDueOnDate(task, today))
                .Select(task => Map(task) with
                {
                    NextRunAt = today.AddMinutes(task.ReminderMinuteOfDay)
                }));
    }

    public async Task<ApiResponse<ScheduledTaskDto>> CreateAsync(
        Guid ownerId, CreateScheduledTaskRequest request, CancellationToken ct = default)
    {
        Validate(request.Title, request.RecurrenceType, request.DaysOfWeek,
            request.ReminderMinuteOfDay, request.StartDate, request.EndDate);
        var days = NormalizeDays(request.DaysOfWeek);
        var task = new ScheduledFarmTask
        {
            OwnerId = ownerId,
            FarmingAreaId = request.FarmingAreaId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            RecurrenceType = request.RecurrenceType.Trim().ToLowerInvariant(),
            DaysOfWeekJson = JsonSerializer.Serialize(days),
            StartDate = (request.StartDate ?? DateTime.UtcNow.Date).Date,
            EndDate = request.EndDate?.Date,
            ReminderMinuteOfDay = request.ReminderMinuteOfDay,
            IsEnabled = request.IsEnabled,
        };
        task.NextRunAt = NextRun(task, DateTime.UtcNow);
        await _uow.ScheduledFarmTasks.AddAsync(task, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<ScheduledTaskDto>.Ok(Map(task), "Scheduled task created.");
    }

    public async Task<ApiResponse<ScheduledTaskDto>> UpdateAsync(
        Guid ownerId, Guid id, UpdateScheduledTaskRequest request, CancellationToken ct = default)
    {
        var task = await FindOwnedAsync(ownerId, id, ct);
        if (request.Title is not null) task.Title = request.Title.Trim();
        if (request.Description is not null) task.Description = request.Description.Trim();
        if (request.FarmingAreaId.HasValue) task.FarmingAreaId = request.FarmingAreaId;
        if (request.RecurrenceType is not null)
            task.RecurrenceType = request.RecurrenceType.Trim().ToLowerInvariant();
        if (request.DaysOfWeek is not null)
            task.DaysOfWeekJson = JsonSerializer.Serialize(NormalizeDays(request.DaysOfWeek));
        if (request.StartDate.HasValue) task.StartDate = request.StartDate.Value.Date;
        if (request.EndDate.HasValue) task.EndDate = request.EndDate.Value.Date;
        if (request.ReminderMinuteOfDay.HasValue)
            task.ReminderMinuteOfDay = request.ReminderMinuteOfDay.Value;
        if (request.IsEnabled.HasValue) task.IsEnabled = request.IsEnabled.Value;
        Validate(task.Title, task.RecurrenceType, ParseDays(task.DaysOfWeekJson),
            task.ReminderMinuteOfDay, task.StartDate, task.EndDate);
        task.NextRunAt = task.IsEnabled ? NextRun(task, DateTime.UtcNow) : null;
        task.UpdatedAt = DateTime.UtcNow;
        _uow.ScheduledFarmTasks.Update(task);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<ScheduledTaskDto>.Ok(Map(task), "Scheduled task updated.");
    }

    public async Task<ApiResponse> DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var task = await FindOwnedAsync(ownerId, id, ct);
        _uow.ScheduledFarmTasks.Remove(task);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Scheduled task deleted.");
    }

    public async Task<ApiResponse<ScheduledTaskDto>> ToggleAsync(
        Guid ownerId, Guid id, bool enabled, CancellationToken ct = default)
    {
        var task = await FindOwnedAsync(ownerId, id, ct);
        task.IsEnabled = enabled;
        task.NextRunAt = enabled ? NextRun(task, DateTime.UtcNow) : null;
        task.UpdatedAt = DateTime.UtcNow;
        _uow.ScheduledFarmTasks.Update(task);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<ScheduledTaskDto>.Ok(Map(task), "Scheduled task toggled.");
    }

    private async Task<ScheduledFarmTask> FindOwnedAsync(
        Guid ownerId, Guid id, CancellationToken ct) =>
        await _uow.ScheduledFarmTasks.FirstOrDefaultAsync(
            task => task.Id == id && task.OwnerId == ownerId, ct)
        ?? throw AppException.NotFound("Scheduled task");

    private static void Validate(
        string title, string recurrence, IReadOnlyList<int>? days,
        int minute, DateTime? start, DateTime? end)
    {
        if (string.IsNullOrWhiteSpace(title)) throw AppException.BadRequest("Title is required.");
        if (recurrence is not ("once" or "daily" or "weekly"))
            throw AppException.BadRequest("RecurrenceType must be once, daily, or weekly.");
        if (minute is < 0 or > 1439)
            throw AppException.BadRequest("ReminderMinuteOfDay must be between 0 and 1439.");
        if (recurrence == "weekly" && NormalizeDays(days).Count == 0)
            throw AppException.BadRequest("Weekly tasks require at least one day.");
        if (start.HasValue && end.HasValue && end.Value.Date < start.Value.Date)
            throw AppException.BadRequest("EndDate must be on or after StartDate.");
    }

    private static List<int> NormalizeDays(IEnumerable<int>? days) =>
        (days ?? Array.Empty<int>()).Where(day => day is >= 1 and <= 7).Distinct().OrderBy(day => day).ToList();

    private static List<int> ParseDays(string json) =>
        JsonSerializer.Deserialize<List<int>>(json) ?? [];

    private static DateTime? NextRun(ScheduledFarmTask task, DateTime now)
    {
        var start = task.StartDate.Date;
        var baseDate = now.Date < start ? start : now.Date;
        var minute = TimeSpan.FromMinutes(task.ReminderMinuteOfDay);
        if (task.RecurrenceType == "once")
        {
            var once = baseDate + minute;
            return once >= now && (!task.EndDate.HasValue || once.Date <= task.EndDate.Value.Date)
                ? once
                : null;
        }

        for (var offset = 0; offset <= 370; offset++)
        {
            var candidate = baseDate.AddDays(offset) + minute;
            if (candidate < now || candidate.Date < start) continue;
            if (task.EndDate.HasValue && candidate.Date > task.EndDate.Value.Date) return null;
            if (task.RecurrenceType == "daily" ||
                ParseDays(task.DaysOfWeekJson).Contains(IsoDay(candidate.DayOfWeek)))
                return candidate;
        }
        return null;
    }

    private static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static bool IsDueOnDate(ScheduledFarmTask task, DateTime date)
    {
        if (date.Date < task.StartDate.Date ||
            task.EndDate.HasValue && date.Date > task.EndDate.Value.Date)
            return false;
        if (task.RecurrenceType == "once") return date.Date == task.StartDate.Date;
        if (task.RecurrenceType == "daily") return true;
        return ParseDays(task.DaysOfWeekJson).Contains(IsoDay(date.DayOfWeek));
    }

    private static ScheduledTaskDto Map(ScheduledFarmTask task) =>
        new(task.Id, task.OwnerId, task.FarmingAreaId, task.Title, task.Description,
            task.RecurrenceType, ParseDays(task.DaysOfWeekJson), task.StartDate,
            task.EndDate, task.ReminderMinuteOfDay, task.IsEnabled, task.NextRunAt);
}
