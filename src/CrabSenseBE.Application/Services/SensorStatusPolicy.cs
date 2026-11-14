using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
namespace CrabSenseBE.Application.Services;
public static class SensorStatusPolicy
{
    public static string Status(Sensor s, Device? d, ControllerPort? p, bool readError = false)
    {
        if (!s.IsActive) return "Disabled";
        if (s.DeviceId.HasValue && (d == null || d.Status == DeviceStatus.Maintenance || d.EdgeState == "Revoked")) return "Offline";
        if ((s.ControllerPortId.HasValue || s.BusPortId.HasValue) && (p == null || !p.IsPresent || d == null || p.ConfigVersion != d.CapabilityVersion)) return "Offline";
        if (readError) return "Error";
        if (s.LastSeenAt is null) return "Unknown";
        return s.LastSeenAt < DateTime.UtcNow.AddSeconds(-(s.DisconnectTimeoutSeconds ?? 900)) ? "Offline" : "Online";
    }

}
