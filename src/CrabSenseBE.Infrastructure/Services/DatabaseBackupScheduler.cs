using System.Globalization;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrabSenseBE.Infrastructure.Services;

public sealed class DatabaseBackupScheduler(
    IServiceScopeFactory scopes, DatabaseBackupService backup,
    IConfiguration config, ILogger<DatabaseBackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give startup and migration a chance to finish before querying schedule.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Backup scheduler check failed"); }
        }
    }

    internal static DateTime? DueSlotUtc(DateTime utcNow, string frequency, string time, TimeZoneInfo zone)
    {
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            || frequency is not ("Daily" or "Weekly")) return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var date = DateOnly.FromDateTime(local);
        if (frequency == "Weekly")
            date = date.AddDays(-(((int)local.DayOfWeek + 6) % 7));
        var due = date.ToDateTime(at, DateTimeKind.Unspecified);
        if (due > local || zone.IsInvalidTime(due)) return null;
        return TimeZoneInfo.ConvertTimeToUtc(due, zone);
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        if (backup.IsMaintenanceMode) return;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.BackupScheduleSettings.AsNoTracking()
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (row is null || !row.Enabled) return;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config["Farm:TimeZone"] ?? "Asia/Ho_Chi_Minh");
        var due = DueSlotUtc(DateTime.UtcNow, row.Frequency, row.BackupTime, zone);
        if (due is not DateTime slot || row.LastAttemptSlotUtc >= slot) return;

        // Atomic persisted claim; no duplicate scheduled execution even if
        // multiple API processes observe this same due slot.
        var claimed = await db.BackupScheduleSettings.Where(x => x.Id == row.Id && x.Enabled
            && x.Frequency == row.Frequency && x.BackupTime == row.BackupTime
            && x.RetentionDays == row.RetentionDays
            && (x.LastAttemptSlotUtc == null || x.LastAttemptSlotUtc < slot))
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.LastAttemptSlotUtc, (DateTime?)slot)
                .SetProperty(x => x.LastResult, "Started"), ct);
        if (claimed != 1) return;
        try
        {
            var result = await backup.CreateScheduledAsync(row.RetentionDays, ct);
            await db.BackupScheduleSettings.Where(x => x.Id == row.Id && x.LastAttemptSlotUtc == slot)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.LastCompletedAtUtc, (DateTime?)DateTime.UtcNow)
                    .SetProperty(x => x.LastResult, "Succeeded")
                    .SetProperty(x => x.LastBackupFile, result.Info.FileName), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled database backup failed");
            await db.BackupScheduleSettings.Where(x => x.Id == row.Id && x.LastAttemptSlotUtc == slot)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.LastResult, "Failed"), ct);
        }
    }
}
