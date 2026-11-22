using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
namespace CrabSenseBE.Application.Services;
public static class SensorStatusPolicy
{
    public static string Status(Sensor s, Device? d, ControllerPort? p, bool readError = false, DateTime? nowUtc = null, string? upstreamStatus = null)
    {
        var now=nowUtc??DateTime.UtcNow;
        if (!s.IsActive) return "Disabled";
        if (s.DeviceId.HasValue && (d == null || d.Status == DeviceStatus.Maintenance || d.EdgeState is "Revoked" or "Rejected")) return "Offline";
        if (s.DeviceId.HasValue && (d?.EdgeState is "Pending" or "Unprovisioned" || upstreamStatus=="Unknown")) return "Unknown";
        if ((s.ControllerPortId.HasValue || s.BusPortId.HasValue) && (p == null || !p.IsPresent || d == null || p.DeviceId!=d.Id || p.ConfigVersion != d.CapabilityVersion)) return "Offline";
        if (upstreamStatus is "Offline" or "Maintenance" or "Disabled" or "Error") return "Offline";
        if (readError) return "Error";
        if (s.LastSeenAt is null || s.LastSeenAt>now.AddSeconds(5)) return "Unknown";
        return s.LastSeenAt < now.AddSeconds(-(s.DisconnectTimeoutSeconds ?? 900)) ? "Offline" : "Online";
    }
}
