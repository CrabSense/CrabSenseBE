using CrabSenseBE.Domain.Common;

namespace CrabSenseBE.Domain.Entities;

/// <summary>Recurring farm task/reminder owned by a farm owner.</summary>
public class ScheduledFarmTask : BaseEntity
{
    public Guid OwnerId { get; set; }
    public Guid? FarmingAreaId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RecurrenceType { get; set; } = "once";
    public string DaysOfWeekJson { get; set; } = "[]";
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EndDate { get; set; }
    public int ReminderMinuteOfDay { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? NextRunAt { get; set; }
}
