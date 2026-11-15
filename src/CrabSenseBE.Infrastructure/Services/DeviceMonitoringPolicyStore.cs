using CrabSenseBE.Application.Common;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace CrabSenseBE.Infrastructure.Services;
public sealed class DeviceMonitoringPolicyStore(AppDbContext db):IMonitoringTimeoutProvider
{
 public async Task<MonitoringTimeouts> GetAsync(CancellationToken ct=default){
    var rows=await db.DeviceMonitoringPolicies.AsNoTracking().ToDictionaryAsync(x=>x.SourceType,x=>x.HeartbeatTimeoutSeconds,ct);
    return new(rows.GetValueOrDefault("camera",900),rows.GetValueOrDefault("controller",90),rows.GetValueOrDefault("kiosk",120));
 }
}
