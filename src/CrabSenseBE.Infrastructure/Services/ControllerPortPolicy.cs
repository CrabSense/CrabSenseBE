using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Infrastructure.Services;

public sealed class ControllerPortPolicy(AppDbContext db) : IControllerPortPolicy
{
    public async Task<PortCommandDecision?> CheckAsync(Guid deviceId, string? channel, string command, CancellationToken ct)
    {
        var header = await db.ControllerPortInventories.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == deviceId, ct);
        if (header is null) return null;
        // Emergency alloff is not dependent on a particular channel.
        if (command == "alloff") return new(header.Revision, true);
        if (header.ReportedAt < DateTime.UtcNow.AddMinutes(-2) || channel is null)
            return new(header.Revision, false);
        var port = await db.ControllerPorts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.DeviceId == deviceId && x.Channel == channel && x.Present, ct);
        var allowed = port is not null && port.Direction == "output"
            && port.Kind is "ssr" or "relay"
            && (command == "off" || (command == "on" && port.RemoteControl));
        return new(header.Revision, allowed);
    }
}
