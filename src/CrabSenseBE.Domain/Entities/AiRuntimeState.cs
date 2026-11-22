namespace CrabSenseBE.Domain.Entities;
public static class AiRuntimeState
{
    public static string Health(AiManagedService row, DateTime now)
    {
        if (!row.IsEnabled) return "Disabled";
        if (row.LastHeartbeatAt is null) return "NotIntegrated";
        if (row.LastHeartbeatAt < now.AddSeconds(-90)) return "Offline";
        if (row.ReportedState == "Running") return row.ReportedVersion == row.Version && row.StartedAt.HasValue ? "Online" : "Degraded";
        return row.ReportedState == "Stopped" ? "Stopped" : "Degraded";
    }
}
