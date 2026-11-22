using System.Security.Claims;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using CrabSenseBE.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/camera-schedules")]
public sealed class CameraSchedulesController(AppDbContext db, CameraRecordingRetentionService? retention = null) : ControllerBase
{
    public record Slot(int Day, string Start, string End);
    public record ScheduleRequest(string Name, string Mode, bool Enabled, string Timezone,
        int RetentionDays, string? VideoQuality, int? Fps, int? MaxStorageGb,
        Guid[] CameraIds, Slot[]? Slots, int? ExpectedVersion = null, bool AutoDeleteExpired = false);
    public record StatusRequest(bool Enabled, int ExpectedVersion);
    public record DuplicateRequest(string Name, Guid[] CameraIds);

    private Guid Actor() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id)
        ? id : throw AppException.Unauthorized();
    private void Audit(Guid id, string action) => db.OperationLogs.Add(new OperationLog
        { UserId = Actor(), EntityType = "CameraSchedule", EntityId = id, Action = action });
    private static IActionResult Result(object value) => new OkObjectResult(ApiResponse<object>.Ok(value));
    private static void Page(int page, int size)
    {
        if (page < 1 || size is < 1 or > 200 || (long)(page - 1) * size > int.MaxValue)
            throw AppException.BadRequest("Invalid pagination.");
    }
    private async Task<CameraSchedule> Require(Guid id, CancellationToken ct) =>
        await db.CameraSchedules.SingleOrDefaultAsync(x => x.Id == id && !x.Deleted, ct)
        ?? throw AppException.NotFound("Camera schedule");
    private static void Version(CameraSchedule s, int? version)
    {
        if (version != s.Version) throw AppException.Conflict("Schedule changed; reload before saving.");
    }
    public static List<CameraScheduleSlot> Normalize(ScheduleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 120 ||
            r.Mode is not ("Continuous" or "Scheduled") ||
            string.IsNullOrWhiteSpace(r.Timezone) || r.Timezone.Length > 100 ||
            r.RetentionDays is < 1 or > 3650 || r.Fps is < 1 or > 120 || r.MaxStorageGb is < 1 ||
            r.VideoQuality?.Length > 32 || r.CameraIds is null || r.CameraIds.Length is < 1 or > 200 ||
            r.CameraIds.Distinct().Count() != r.CameraIds.Length || r.CameraIds.Contains(Guid.Empty))
            throw AppException.BadRequest("Invalid schedule configuration.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(r.Timezone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw AppException.BadRequest("Invalid timezone."); }
        var slots = new List<CameraScheduleSlot>();
        if (r.Mode == "Continuous") return slots;
        if (r.Slots is null || r.Slots.Length is < 1 or > 168)
            throw AppException.BadRequest("Scheduled mode requires 1–168 slots.");
        foreach (var s in r.Slots)
        {
            if (s.Day is < 1 or > 7 || !TimeOnly.TryParseExact(s.Start, "HH:mm", out var start) ||
                !(s.End == "24:00" || TimeOnly.TryParseExact(s.End, "HH:mm", out _)))
                throw AppException.BadRequest("Invalid day or time.");
            var a = start.Hour * 60 + start.Minute;
            var end = s.End == "24:00" ? 1440 : (int)TimeOnly.ParseExact(s.End, "HH:mm").ToTimeSpan().TotalMinutes;
            if (a == end) throw AppException.BadRequest("Start and end cannot be equal.");
            void Add(int day, int lo, int hi)
            {
                if (lo == hi) return;
                if (slots.Any(x => x.Day == day && lo < x.EndMinute && hi > x.StartMinute))
                    throw AppException.BadRequest("Overlapping recording slots.");
                slots.Add(new CameraScheduleSlot { Day = day, StartMinute = lo, EndMinute = hi });
            }
            if (end > a) Add(s.Day, a, end);
            else { Add(s.Day, a, 1440); Add(s.Day == 7 ? 1 : s.Day + 1, 0, end); }
        }
        return slots.OrderBy(x => x.Day).ThenBy(x => x.StartMinute).ToList();
    }
    private static string Clock(int minute) => minute == 1440 ? "24:00" : $"{minute / 60:00}:{minute % 60:00}";
    public static bool IsDue(CameraSchedule s,IEnumerable<CameraScheduleSlot> slots,DateTime utc)=>CameraScheduleClock.IsDue(s,slots,utc);
    public static DateTime? NextDue(CameraSchedule s,IReadOnlyCollection<CameraScheduleSlot> slots,DateTime utc)=>CameraScheduleClock.NextDue(s,slots,utc);
    private async Task<List<CameraScheduleSlot>> ValidateRequest(ScheduleRequest r, Guid? id, CancellationToken ct)
    {
        var slots = Normalize(r);
        if (await CameraRecordingLibrary.CameraDevices(db).CountAsync(x => r.CameraIds.Contains(x.Id), ct) != r.CameraIds.Length)
            throw AppException.BadRequest("Unknown or non-camera device.");
        if (await db.CameraScheduleAssignments.AnyAsync(x => r.CameraIds.Contains(x.DeviceId) && x.ScheduleId != id, ct))
            throw AppException.Conflict("Camera already belongs to another schedule; unassign it first.");
        return slots;
    }
    private async Task<object> Map(CameraSchedule s, CancellationToken ct)
    {
        var ids = await db.CameraScheduleAssignments.Where(x => x.ScheduleId == s.Id).Select(x => x.DeviceId).ToListAsync(ct);
        var slots = await db.CameraScheduleSlots.Where(x => x.ScheduleId == s.Id).OrderBy(x => x.Day).ThenBy(x => x.StartMinute).ToListAsync(ct);
        var cutoff = DateTime.UtcNow.AddSeconds(-90);
        var recording = await db.CameraRecordingExecutions.CountAsync(x => x.ScheduleId == s.Id && x.Status == "Recording" && x.LastHeartbeatAt >= cutoff, ct);
        return new { s.Id, s.Name, s.Mode, s.Enabled, s.Timezone, s.RetentionDays, s.VideoQuality, s.Fps, s.MaxStorageGb,
            s.Version, s.CreatedAt, s.UpdatedAt, s.AutoDeleteExpired, cameraIds = ids, slots = slots.Select(x => new Slot(x.Day, Clock(x.StartMinute), Clock(x.EndMinute))),
            scheduledNow = IsDue(s, slots, DateTime.UtcNow), nextRecordingAt=NextDue(s,slots,DateTime.UtcNow), recordingCount = recording };
    }
    private async Task Project(CameraSchedule s, Guid[] ids, List<CameraScheduleSlot> slots, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            var row = await db.CameraRecordingSettings.FirstOrDefaultAsync(x => x.DeviceId == id, ct);
            if (row is null) { row = new CameraRecordingSetting { DeviceId = id }; db.CameraRecordingSettings.Add(row); }
            row.Mode = s.Enabled ? s.Mode : "Disabled";
            row.RetentionDays = s.RetentionDays; row.VideoQuality = s.VideoQuality; row.Fps = s.Fps; row.MaxStorageGb = s.MaxStorageGb;
            row.WeeklyScheduleJson = s.Mode == "Scheduled" ? JsonSerializer.Serialize(new { timezone = s.Timezone,
                slots = slots.Select(x => new { day = x.Day, start = Clock(x.StartMinute), end = Clock(x.EndMinute) }) }) : null;
        }
    }
    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("Schedule or camera assignment changed; reload."); }
    }
    [HttpGet]
    public async Task<IActionResult> List(string? search, bool? enabled, Guid? cameraId, Guid? farmingAreaId,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        Page(page,pageSize);
        var q = db.CameraSchedules.AsNoTracking().Where(x => !x.Deleted);
        if (!string.IsNullOrWhiteSpace(search)) { var key = search.Trim().ToLower(); q = q.Where(x => x.Name.ToLower().Contains(key)); }
        if (enabled.HasValue) q = q.Where(x => x.Enabled == enabled.Value);
        if (cameraId.HasValue) q = q.Where(x => db.CameraScheduleAssignments.Any(a => a.ScheduleId == x.Id && a.DeviceId == cameraId));
        if (farmingAreaId.HasValue) q = q.Where(x => db.CameraScheduleAssignments.Any(a => a.ScheduleId == x.Id && db.Devices.Any(d => d.Id == a.DeviceId && d.FarmingAreaId == farmingAreaId)));
        var totalCount = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct);
        var items = new List<object>(); foreach(var s in rows) items.Add(await Map(s,ct));
        return Result(new { items, totalCount, page, pageSize });
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => Result(await Map(await Require(id,ct),ct));
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(ScheduleRequest request, Guid? scheduleId, CancellationToken ct)
    { if(scheduleId.HasValue) _ = await Require(scheduleId.Value,ct); var slots = await ValidateRequest(request,scheduleId,ct); return Result(new { valid = true, slots = slots.Select(x => new Slot(x.Day,Clock(x.StartMinute),Clock(x.EndMinute))) }); }
    [HttpPost]
    public Task<IActionResult> Create(ScheduleRequest request, CancellationToken ct) => Write(null,request,ct);
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, ScheduleRequest request, CancellationToken ct) => Write(id,request,ct);
    private async Task<IActionResult> Write(Guid? id, ScheduleRequest r, CancellationToken ct)
    {
        var s = id.HasValue ? await Require(id.Value,ct) : new CameraSchedule();
        if(id.HasValue) Version(s,r.ExpectedVersion);
        var slots = await ValidateRequest(r,id,ct);
        var old = await db.CameraScheduleAssignments.Where(x => x.ScheduleId == s.Id).ToListAsync(ct);
        var removed = old.Where(x => !r.CameraIds.Contains(x.DeviceId)).ToList();
        db.CameraScheduleAssignments.RemoveRange(removed);
        foreach(var a in removed)
        { var setting = await db.CameraRecordingSettings.FirstOrDefaultAsync(x => x.DeviceId == a.DeviceId,ct); if(setting != null) setting.Mode = "Disabled"; }
        foreach(var camera in r.CameraIds.Where(x => !old.Any(a => a.DeviceId == x)))
            db.CameraScheduleAssignments.Add(new CameraScheduleAssignment { ScheduleId = s.Id, DeviceId = camera });
        db.CameraScheduleSlots.RemoveRange(await db.CameraScheduleSlots.Where(x => x.ScheduleId == s.Id).ToListAsync(ct));
        foreach(var slot in slots) { slot.ScheduleId = s.Id; db.CameraScheduleSlots.Add(slot); }
        s.Name=r.Name.Trim(); s.Mode=r.Mode; s.Enabled=r.Enabled; s.Timezone=r.Timezone;
        s.RetentionDays=r.RetentionDays; s.AutoDeleteExpired=r.AutoDeleteExpired; s.VideoQuality=r.VideoQuality; s.Fps=r.Fps; s.MaxStorageGb=r.MaxStorageGb;
        if(id.HasValue) s.Version++; else db.CameraSchedules.Add(s);
        await Project(s,r.CameraIds,slots,ct); Audit(s.Id,id.HasValue?"UpdateCameraSchedule":"CreateCameraSchedule"); await Save(ct);
        return Result(await Map(s,ct));
    }
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, StatusRequest r, CancellationToken ct)
    {
        var s=await Require(id,ct); Version(s,r.ExpectedVersion); s.Enabled=r.Enabled; s.Version++;
        await Project(s,await db.CameraScheduleAssignments.Where(x=>x.ScheduleId==id).Select(x=>x.DeviceId).ToArrayAsync(ct),
            await db.CameraScheduleSlots.Where(x=>x.ScheduleId==id).ToListAsync(ct),ct);
        Audit(id,"ChangeCameraScheduleStatus"); await Save(ct); return Result(await Map(s,ct));
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id,int expectedVersion,CancellationToken ct)
    {
        var s=await Require(id,ct); Version(s,expectedVersion); s.Enabled=false;s.Deleted=true;s.Version++;
        var assignments=await db.CameraScheduleAssignments.Where(x=>x.ScheduleId==id).ToListAsync(ct);
        await Project(s,assignments.Select(x=>x.DeviceId).ToArray(),await db.CameraScheduleSlots.Where(x=>x.ScheduleId==id).ToListAsync(ct),ct);
        db.CameraScheduleAssignments.RemoveRange(assignments); Audit(id,"DeleteCameraSchedule"); await Save(ct);
        return Result(new { id, deleted=true });
    }
    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id,DuplicateRequest r,CancellationToken ct)
    {
        var s=await Require(id,ct);var slots=await db.CameraScheduleSlots.Where(x=>x.ScheduleId==id).ToListAsync(ct);
        return await Write(null,new ScheduleRequest(r.Name,s.Mode,false,s.Timezone,s.RetentionDays,s.VideoQuality,s.Fps,s.MaxStorageGb,r.CameraIds,
            slots.Select(x=>new Slot(x.Day,Clock(x.StartMinute),Clock(x.EndMinute))).ToArray(),AutoDeleteExpired:false),ct);
    }
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var q=db.CameraSchedules.Where(x=>!x.Deleted);var now=DateTime.UtcNow.AddSeconds(-90);
        return Result(new { total=await q.CountAsync(ct), enabled=await q.CountAsync(x=>x.Enabled,ct), paused=await q.CountAsync(x=>!x.Enabled,ct),
            recordingCameras=await db.CameraRecordingExecutions.Where(x=>x.Status=="Recording"&&x.LastHeartbeatAt>=now).Select(x=>x.DeviceId).Distinct().CountAsync(ct),
            failedExecutions=await db.CameraRecordingExecutions.CountAsync(x=>x.Status=="Failed",ct) });
    }
    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar(Guid? cameraId,CancellationToken ct,Guid? farmingAreaId=null)
    {
        var q=db.CameraSchedules.AsNoTracking().Where(x=>!x.Deleted);
        if(cameraId.HasValue) q=q.Where(x=>db.CameraScheduleAssignments.Any(a=>a.ScheduleId==x.Id&&a.DeviceId==cameraId));
        if(farmingAreaId.HasValue)q=q.Where(x=>db.CameraScheduleAssignments.Any(a=>a.ScheduleId==x.Id&&db.Devices.Any(d=>d.Id==a.DeviceId&&d.FarmingAreaId==farmingAreaId)));
        var items=new List<object>();foreach(var s in await q.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Take(500).ToListAsync(ct)) items.Add(await Map(s,ct));
        return Result(new { items, truncated=await q.CountAsync(ct)>500 });
    }
    [HttpGet("{id:guid}/retention")]
    public async Task<IActionResult> Retention(Guid id,CancellationToken ct)
    { var s=await Require(id,ct); return Result(await (retention??throw new AppException("Retention service unavailable.",503)).Preview(s,ct)); }
    [HttpPost("{id:guid}/retention/cleanup")]
    public async Task<IActionResult> Cleanup(Guid id,int expectedVersion,bool confirm,CancellationToken ct)
    { if(!confirm) throw AppException.BadRequest("Explicit cleanup confirmation required."); var s=await Require(id,ct); Version(s,expectedVersion); return Result(await (retention??throw new AppException("Retention service unavailable.",503)).Cleanup(s.Id,s.Version,Actor(),false,ct)); }
    [HttpGet("{id:guid}/executions")]
    public async Task<IActionResult> Executions(Guid id,int page=1,int pageSize=25,CancellationToken ct=default)
    {
        Page(page,pageSize);if(!await db.CameraSchedules.AnyAsync(x=>x.Id==id,ct))throw AppException.NotFound("Camera schedule");
        var q=db.CameraRecordingExecutions.AsNoTracking().Where(x=>x.ScheduleId==id);
        return Result(new { items=await q.OrderByDescending(x=>x.StartedAt).ThenByDescending(x=>x.Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync(ct),totalCount=await q.CountAsync(ct),page,pageSize });
    }
}
