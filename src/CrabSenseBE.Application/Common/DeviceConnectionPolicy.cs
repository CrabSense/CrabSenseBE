using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
namespace CrabSenseBE.Application.Common;
public sealed record MonitoringTimeouts(int CameraSeconds=900,int ControllerSeconds=90,int KioskSeconds=120,int LegacyControllerSeconds=900)
{
 public int ForDevice(Device d)=>CameraConfiguration.IsCamera(d)?CameraSeconds:d.KioskId.HasValue||!string.IsNullOrWhiteSpace(d.CredentialHash)?ControllerSeconds:LegacyControllerSeconds;
}
public interface IMonitoringTimeoutProvider { Task<MonitoringTimeouts> GetAsync(CancellationToken ct=default); }
public static class DeviceConnectionPolicy
{
 public static string Fresh(DateTime? seen,DateTime now,int seconds)=>seen==null||seen>now.AddSeconds(5)?"Unknown":seen<now.AddSeconds(-seconds)?"Offline":"Online";
 public static string DeviceStatusOf(Device d,DateTime now,MonitoringTimeouts t){
    if(d.Status==DeviceStatus.Maintenance)return "Maintenance";
    if(d.EdgeState is "Rejected" or "Revoked")return "Disabled";
    if(d.EdgeState is "Unprovisioned" or "Pending")return "Unknown";
    var fresh=Fresh(d.LastSeenAt,now,t.ForDevice(d));
    return fresh!="Online"?fresh:d.Status==DeviceStatus.Online?"Online":d.Status.ToString();
 }
 public static string KioskStatusOf(FarmKiosk k,DateTime now,MonitoringTimeouts t)=>k.Status=="Revoked"?"Disabled":k.Status=="Created"?"Unknown":Fresh(k.LastSeenAt,now,t.KioskSeconds);
 public static string SensorStatusOf(Sensor s,Device? d,ControllerPort? p,DateTime now,MonitoringTimeouts t){
    if(!s.IsActive)return "Disabled";
    if(s.DeviceId.HasValue){
        if(d==null)return "Offline";
        var upstream=DeviceStatusOf(d,now,t);
        if(upstream is "Offline" or "Maintenance" or "Disabled")return "Offline";
        if(d.EdgeState is "Pending" or "Unprovisioned")return "Unknown";
    }
    if((s.ControllerPortId.HasValue||s.BusPortId.HasValue)&&(p==null||!p.IsPresent||d==null||p.ConfigVersion!=d.CapabilityVersion))return "Offline";
    return Fresh(s.LastSeenAt,now,s.DisconnectTimeoutSeconds??900);
 }
}
