using CrabSenseBE.Domain.Entities;
namespace CrabSenseBE.Infrastructure.Services;

public sealed record ObservedAvailability(DateTime FromUtc,DateTime ToUtc,long OnlineSeconds,long OfflineSeconds,long OtherSeconds,long UnknownSeconds,double? AvailabilityPercent,double CoveragePercent,string Method="sampled_status_estimate");
public static class DeviceAvailability
{
    public static ObservedAvailability Calculate(IEnumerable<DeviceConnectionEvent> events,DeviceMonitoringState? state,DateTime from,DateTime to){
        var rows=events.OrderBy(x=>x.OccurredAt).ThenBy(x=>x.Id).ToList();
        double online=0,offline=0,other=0;
        for(int i=0;i<rows.Count;i++){
            var e=rows[i];var next=i+1<rows.Count?rows[i+1]:null;
            var end=next==null?state?.ObservedAt??e.OccurredAt:next.ReasonCode=="observation_gap"||next.NewStatus=="Removed"?next.PreviousObservedAt??e.OccurredAt:next.OccurredAt;
            var start=e.OccurredAt>from?e.OccurredAt:from;end=end<to?end:to;
            var seconds=Math.Max(0,(end-start).TotalSeconds);
            if(e.NewStatus=="Online")online+=seconds;else if(e.NewStatus=="Offline")offline+=seconds;else if(e.NewStatus is "Maintenance" or "Disabled" or "Error")other+=seconds;
        }
        var total=Math.Max(0,(to-from).TotalSeconds);
        return new(from,to,(long)online,(long)offline,(long)other,(long)Math.Max(0,total-online-offline-other),online+offline==0?null:Math.Round(100*online/(online+offline),2),total==0?0:Math.Round(100*(online+offline+other)/total,2));
    }
}