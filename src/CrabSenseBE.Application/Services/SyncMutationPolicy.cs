using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.Application.Services;

public static class SyncMutationPolicy
{
    public static bool IsStale(DateTime serverAt, DateTimeOffset? clientAt, DateTimeOffset? baseServerAt, DateTimeOffset now)
    {
        var server = new DateTimeOffset(DateTime.SpecifyKind(serverAt, DateTimeKind.Utc));
        if (clientAt is null || clientAt > now.AddMinutes(5)) return true;
        if (baseServerAt.HasValue) return baseServerAt.Value != server;
        return clientAt <= server;
    }

    public static bool IsBoxStatusConsistent(string status, bool occupied)
    {
        if (status is BoxStatuses.Empty or BoxStatuses.Harvested) return !occupied;
        if (status is BoxStatuses.Active or BoxStatuses.Molting or BoxStatuses.Quarantine) return occupied;
        return status is BoxStatuses.Watch or BoxStatuses.Maintenance;
    }
}
