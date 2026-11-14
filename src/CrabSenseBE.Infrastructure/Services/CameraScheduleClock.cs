using CrabSenseBE.Domain.Entities;
namespace CrabSenseBE.Infrastructure.Services;
public static class CameraScheduleClock
{
    public static bool IsDue(CameraSchedule s, IEnumerable<CameraScheduleSlot> slots, DateTime utc)
    {
        if (!s.Enabled || s.Deleted) return false;
        if (s.Mode == "Continuous") return true;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(s.Timezone));
        var day = ((int)local.DayOfWeek + 6) % 7 + 1;
        var minute = local.Hour * 60 + local.Minute;
        return slots.Any(x => x.Day == day && minute >= x.StartMinute && minute < x.EndMinute);
    }
    public static DateTime? NextDue(CameraSchedule s, IReadOnlyCollection<CameraScheduleSlot> slots, DateTime utc)
    {
        if (!s.Enabled || s.Deleted) return null;
        if (IsDue(s, slots, utc)) return utc;
        var zone=TimeZoneInfo.FindSystemTimeZoneById(s.Timezone);
        var local=TimeZoneInfo.ConvertTimeFromUtc(utc,zone);
        DateTime? next=null;
        for(var offset=0;offset<=7;offset++)
        {
            var date=local.Date.AddDays(offset);
            var day=((int)date.DayOfWeek+6)%7+1;
            foreach(var slot in slots.Where(x=>x.Day==day))
            {
                var start=DateTime.SpecifyKind(date.AddMinutes(slot.StartMinute),DateTimeKind.Unspecified);
                while(zone.IsInvalidTime(start)&&start<date.AddMinutes(slot.EndMinute))start=start.AddMinutes(1);
                if(start>=date.AddMinutes(slot.EndMinute))continue;
                IEnumerable<DateTime> instants=zone.IsAmbiguousTime(start)
                    ?zone.GetAmbiguousTimeOffsets(start).Select(o=>new DateTimeOffset(start,o).UtcDateTime)
                    :new[]{TimeZoneInfo.ConvertTimeToUtc(start,zone)};
                foreach(var candidate in instants)
                    if(candidate>=utc&&(!next.HasValue||candidate<next.Value))next=candidate;
            }
        }
        return next;
    }
}
