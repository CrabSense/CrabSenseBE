using CrabSenseBE.Infrastructure.Services;

namespace CrabSenseBE.UnitTests.Infrastructure;

public sealed class DatabaseBackupSchedulerTests
{
    [Fact]
    public void Daily_and_weekly_slots_use_local_time_and_do_not_run_before_due()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("TestVN", TimeSpan.FromHours(7), "VN", "VN");
        var monday = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(monday.AddHours(-5), DatabaseBackupScheduler.DueSlotUtc(monday, "Daily", "02:00", zone));
        Assert.Null(DatabaseBackupScheduler.DueSlotUtc(monday, "Daily", "08:00", zone));
        Assert.Equal(monday.AddHours(-5), DatabaseBackupScheduler.DueSlotUtc(monday.AddDays(2), "Weekly", "02:00", zone));
        Assert.Null(DatabaseBackupScheduler.DueSlotUtc(monday, "Monthly", "02:00", zone));
    }
}
