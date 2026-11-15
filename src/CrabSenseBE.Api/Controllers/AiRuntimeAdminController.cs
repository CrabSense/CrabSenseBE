using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Platform)]
[Route("api/ai/services/{id:guid}")]
public sealed class AiRuntimeAdminController(AppDbContext db) : ControllerBase
{
    public record Configuration(string Name, string? DeviceLabel, decimal ConfidenceThreshold,
        decimal FrameSamplingFps, int BatchSize, string[] DetectionClasses, bool IsEnabled);
    public record ModelRegistration(string Version, string ArtifactName, string Sha256);

    private async Task<AiManagedService> Service(Guid id, CancellationToken ct) =>
        await db.AiManagedServices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("AI service");

    private void Audit(Guid id, string action, string details)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(claim, out var actor))
            db.OperationLogs.Add(new OperationLog { UserId = actor, Action = action, EntityType = "AiManagedService", EntityId = id, Details = details, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });
    }

    [HttpPut("configuration")]
    public async Task<IActionResult> Configure(Guid id, Configuration request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || request.DeviceLabel?.Length > 200 ||
            request.ConfidenceThreshold is < 0 or > 1 || request.FrameSamplingFps is <= 0 or > 120 ||
            request.BatchSize is < 1 or > 128 || request.DetectionClasses is null || request.DetectionClasses.Length > 100 ||
            request.DetectionClasses.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
            throw AppException.BadRequest("Cấu hình AI không hợp lệ.");
        var row = await Service(id, ct);
        if (row.Status.EndsWith("Requested") && row.CommandRequestedAt > DateTime.UtcNow.AddMinutes(-5))
            throw AppException.Conflict("Đang có lệnh chờ xác nhận.");
        row.Name = name;
        row.DeviceLabel = request.DeviceLabel?.Trim();
        row.ConfidenceThreshold = request.ConfidenceThreshold;
        row.FrameSamplingFps = request.FrameSamplingFps;
        row.BatchSize = request.BatchSize;
        row.DetectionClassesJson = JsonSerializer.Serialize(request.DetectionClasses.Select(x => x.Trim()).Distinct());
        row.IsEnabled = request.IsEnabled;
        row.UpdatedAt = DateTime.UtcNow;
        Audit(id, "UpdateAiConfiguration", "Updated service configuration");
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("Đã lưu cấu hình; worker sẽ đọc cấu hình mới."));
    }

    [HttpGet("models")]
    public async Task<IActionResult> Models(Guid id, CancellationToken ct)
    {
        await Service(id, ct);
        return Ok(ApiResponse<object>.Ok(await db.AiModelVersions.AsNoTracking().Where(x => x.AiServiceId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)));
    }

    [HttpPost("models")]
    public async Task<IActionResult> RegisterModel(Guid id, ModelRegistration request, CancellationToken ct)
    {
        await Service(id, ct);
        var version = request.Version?.Trim() ?? "";
        var artifact = request.ArtifactName?.Trim() ?? "";
        var hash = request.Sha256?.Trim().ToLowerInvariant() ?? "";
        if (!Regex.IsMatch(version, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$") ||
            !Regex.IsMatch(artifact, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,119}\.pt$") ||
            !Regex.IsMatch(hash, @"^[0-9a-f]{64}$"))
            throw AppException.BadRequest("Version, tên file .pt hoặc SHA-256 không hợp lệ.");
        if (await db.AiModelVersions.AnyAsync(x => x.AiServiceId == id && x.Version == version, ct))
            throw AppException.Conflict("Phiên bản đã tồn tại; không ghi đè artifact.");
        var model = new AiModelVersion { AiServiceId = id, Version = version, ArtifactName = artifact, Sha256 = hash };
        db.AiModelVersions.Add(model);
        Audit(id, "RegisterAiModel", version);
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<AiModelVersion>.Ok(model));
    }

    [HttpGet("cameras")]
    public async Task<IActionResult> Cameras(Guid id, CancellationToken ct)
    {
        await Service(id, ct);
        var items = await (from a in db.AiCameraAssignments.AsNoTracking()
                           join d in db.Devices.AsNoTracking() on a.DeviceId equals d.Id
                           where a.AiServiceId == id
                           select new { a.DeviceId, d.DeviceCode, d.Name }).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpPut("cameras/{deviceId:guid}")]
    public async Task<IActionResult> Assign(Guid id, Guid deviceId, CancellationToken ct)
    {
        await Service(id, ct);
        var d = await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deviceId, ct) ?? throw AppException.NotFound("Camera");
        if (!(d.DeviceType ?? "").Contains("cam", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(d.StreamUrl) && string.IsNullOrWhiteSpace(d.SnapshotUrl))
            throw AppException.BadRequest("Thiết bị không phải camera.");
        if (!await db.AiCameraAssignments.AnyAsync(x => x.AiServiceId == id && x.DeviceId == deviceId, ct))
        {
            db.AiCameraAssignments.Add(new AiCameraAssignment { AiServiceId = id, DeviceId = deviceId });
            Audit(id, "AssignAiCamera", deviceId.ToString());
            await db.SaveChangesAsync(ct);
        }
        return Ok(ApiResponse.Ok());
    }

    [HttpDelete("cameras/{deviceId:guid}")]
    public async Task<IActionResult> Unassign(Guid id, Guid deviceId, CancellationToken ct)
    {
        await Service(id, ct);
        var a = await db.AiCameraAssignments.FirstOrDefaultAsync(x => x.AiServiceId == id && x.DeviceId == deviceId, ct);
        if (a is not null) { db.AiCameraAssignments.Remove(a); Audit(id, "UnassignAiCamera", deviceId.ToString()); await db.SaveChangesAsync(ct); }
        return Ok(ApiResponse.Ok());
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs(Guid id, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        await Service(id, ct);
        page = Math.Clamp(page, 1, 100000); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.AiInferenceJobs.AsNoTracking().Where(x => x.AiServiceId == id);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);
        var count = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.DeviceId, x.ModelVersion, x.Status, x.Attempts, x.CreatedAt, x.CompletedAt, x.DurationMs,
                Error = x.Error == null ? null : "Inference failed; see worker logs." }).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { items, totalCount = count, page, pageSize }));
    }

    [HttpPost("jobs/{jobId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, Guid jobId, CancellationToken ct)
    {
        var s = await Service(id, ct);
        var job = await db.AiInferenceJobs.FirstOrDefaultAsync(x => x.Id == jobId && x.AiServiceId == id, ct) ?? throw AppException.NotFound("AI job");
        if (job.Status != "Failed" || job.ModelVersion != s.Version || !s.IsEnabled)
            throw AppException.Conflict("Chỉ thử lại job lỗi của model đang kích hoạt.");
        job.Status = "Queued"; job.Attempts = 0; job.LeaseId = null; job.LeaseUntil = null; job.Error = null; job.CompletedAt = null;
        Audit(id, "RetryAiInference", jobId.ToString());
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("Đã đưa job trở lại hàng đợi."));
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> Metrics(Guid id, CancellationToken ct)
    {
        await Service(id, ct);
        var from = DateTime.UtcNow.AddHours(-24);
        var q = db.AiInferenceJobs.AsNoTracking().Where(x => x.AiServiceId == id && x.CompletedAt >= from);
        var completed = await q.CountAsync(x => x.Status == "Succeeded", ct);
        var failed = await q.CountAsync(x => x.Status == "Failed", ct);
        var average = await q.Where(x => x.Status == "Succeeded" && x.DurationMs != null).AverageAsync(x => (double?)x.DurationMs, ct);
        return Ok(ApiResponse<object>.Ok(new { completed, failed, averageDurationMs = average, windowHours = 24,
            failureRate = completed + failed == 0 ? (double?)null : (double)failed / (completed + failed) }));
    }

    [HttpGet("audit")]
    public async Task<IActionResult> History(Guid id, CancellationToken ct)
    {
        await Service(id, ct);
        var items = await db.OperationLogs.AsNoTracking().Where(x => x.EntityType == "AiManagedService" && x.EntityId == id)
            .OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Action, x.Details, x.UserId, x.CreatedAt }).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(items));
    }
}
