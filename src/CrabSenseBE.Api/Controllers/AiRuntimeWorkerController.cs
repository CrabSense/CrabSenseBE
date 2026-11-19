using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

// Each worker receives a separate server-configured credential; it cannot use platform APIs.
[ApiController]
[AllowAnonymous]
[Route("api/ai/runtime/{id:guid}")]
public sealed class AiRuntimeWorkerController(AppDbContext db, IConfiguration config, IMediaStorageService storage) : ControllerBase
{
    public record HeartbeatRequest(string State, string? Version);
    public record AckRequest(Guid CommandId, bool Success, string? Version, string? Error);
    public record ResultRequest(Guid LeaseId, bool Success, string? Label, decimal? Confidence, long? DurationMs, string? Error, JsonElement? Evidence);

    private async Task<AiManagedService> Authenticate(Guid id, CancellationToken ct)
    {
        var key = config[$"AiRuntime:Workers:{id:D}:Key"];
        var provided = Request.Headers["X-AI-Worker-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || provided.Length > 512 ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), SHA256.HashData(Encoding.UTF8.GetBytes(provided))))
            throw AppException.Unauthorized("Invalid AI worker credential.");
        return await db.AiManagedServices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("AI service");
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid id, HeartbeatRequest request, CancellationToken ct)
    {
        var s = await Authenticate(id, ct);
        if (request.State is not ("Running" or "Stopped" or "Error") || request.Version?.Length > 80)
            throw AppException.BadRequest("Invalid runtime state.");
        var now = DateTime.UtcNow;
        s.LastHeartbeatAt = now; s.ReportedState = request.State; s.ReportedVersion = request.Version;
        if (s.Status.EndsWith("Requested") && s.CommandRequestedAt <= now.AddMinutes(-5))
        { s.Status = "Failed"; s.CommandError = "Command acknowledgement expired."; s.CommandId = null; }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Command changed; retry heartbeat."); }
        AiModelVersion? model = null;
        if (s.RequestedVersion is not null && s.Status.EndsWith("Requested"))
            model = await db.AiModelVersions.AsNoTracking().FirstOrDefaultAsync(x => x.AiServiceId == id && x.Version == s.RequestedVersion, ct);
        return Ok(ApiResponse<object>.Ok(new { enabled = s.IsEnabled,
            command = s.Status.EndsWith("Requested") && s.CommandId.HasValue ? new { id = s.CommandId, action = s.Status.Replace("Requested", ""), model } : null }));
    }

    [HttpPost("ack")]
    public async Task<IActionResult> Ack(Guid id, AckRequest request, CancellationToken ct)
    {
        var s = await Authenticate(id, ct);
        if (s.CommandId != request.CommandId || !s.Status.EndsWith("Requested") || s.CommandRequestedAt <= DateTime.UtcNow.AddMinutes(-5))
            throw AppException.Conflict("Stale AI command.");
        if (request.Success)
        {
            if (s.Status == "StopRequested")
            {
                s.Status = "Stopped"; s.ReportedState = "Stopped"; s.ReportedVersion = null; s.StartedAt = null;
            }
            else
            {
                if (!s.IsEnabled || request.Version != s.RequestedVersion || string.IsNullOrWhiteSpace(request.Version))
                    throw AppException.Conflict("Worker confirmed a different model version.");
                if (s.Version != request.Version) { s.PreviousVersion = s.Version; s.Version = request.Version; }
                s.Status = "Running"; s.ReportedState = "Running"; s.ReportedVersion = request.Version; s.StartedAt = DateTime.UtcNow;
            }
            s.CommandError = null;
        }
        else
        {
            s.Status = "Failed"; s.CommandError = Limit(request.Error, 500) ?? "Worker rejected command.";
        }
        s.CommandId = null; // compare-and-swap prevents duplicate/stale acknowledgements
        s.LastHeartbeatAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Command changed."); }
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("claim")]
    public async Task<IActionResult> Claim(Guid id, CancellationToken ct)
    {
        var s = await Authenticate(id, ct);
        var now = DateTime.UtcNow;
        if (AiRuntimeState.Health(s, now) != "Online" || s.Status.EndsWith("Requested")) return Ok(ApiResponse<object?>.Ok(null));
        // Expired leases are retried at most three times; old model jobs are never silently run on a new model.
        await db.AiInferenceJobs.Where(x => x.AiServiceId == id && x.ModelVersion != s.Version &&
            (x.Status == "Queued" || (x.Status == "Running" && x.LeaseUntil < now)))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "Failed").SetProperty(x => x.Error, "Model superseded; submit a job for the active version.").SetProperty(x => x.CompletedAt, now).SetProperty(x => x.LeaseId, (Guid?)null), ct);
        await db.AiInferenceJobs.Where(x => x.AiServiceId == id && x.Status == "Running" && x.LeaseUntil < now && x.Attempts >= 3)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "Failed").SetProperty(x => x.Error, "Worker lease expired.").SetProperty(x => x.CompletedAt, now).SetProperty(x => x.LeaseId, (Guid?)null), ct);
        var candidate = await db.AiInferenceJobs.AsNoTracking().Where(x => x.AiServiceId == id && x.ModelVersion == s.Version && x.Attempts < 3 &&
            (x.Status == "Queued" || (x.Status == "Running" && x.LeaseUntil < now))).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (candidate is null) return Ok(ApiResponse<object?>.Ok(null));
        var lease = Guid.NewGuid();
        var changed = await db.AiInferenceJobs.Where(x => x.Id == candidate.Id && x.Attempts < 3 &&
            (x.Status == "Queued" || (x.Status == "Running" && x.LeaseUntil < now)))
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "Running").SetProperty(x => x.LeaseId, lease)
                .SetProperty(x => x.LeaseUntil, now.AddMinutes(10)).SetProperty(x => x.Attempts, x => x.Attempts + 1), ct);
        if (changed != 1) return Ok(ApiResponse<object?>.Ok(null));
        var job = await db.AiInferenceJobs.AsNoTracking().FirstAsync(x => x.Id == candidate.Id, ct);
        var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.MediaId, ct);
        return Ok(ApiResponse<object>.Ok(new { job, category = media?.Category }));
    }

    private async Task<AiInferenceJob> Leased(Guid id, Guid jobId, Guid lease, CancellationToken ct)
    {
        var job = await db.AiInferenceJobs.FirstOrDefaultAsync(x => x.Id == jobId && x.AiServiceId == id, ct) ?? throw AppException.NotFound("AI job");
        if (job.Status != "Running" || job.LeaseId != lease || job.LeaseUntil <= DateTime.UtcNow)
            throw AppException.Conflict("Invalid or expired job lease.");
        return job;
    }

    [HttpGet("jobs/{jobId:guid}/content")]
    public async Task<IActionResult> Content(Guid id, Guid jobId, [FromQuery] Guid lease, CancellationToken ct)
    {
        await Authenticate(id, ct);
        var job = await Leased(id, jobId, lease, ct);
        var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.MediaId, ct) ?? throw AppException.NotFound("Media");
        if (media.DeviceId != job.DeviceId || media.BoxId != job.BoxId || media.SizeBytes > 100L * 1024 * 1024 ||
            !string.Equals(media.Provider, storage.ProviderName, StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict("Media source or storage provider changed.");
        if (!media.IsShared) await storage.EnsurePrivateAccessAsync(media.StorageKey, ct);
        return File(await storage.DownloadAsync(media.StorageKey, ct), media.ContentType);
    }

    [HttpPost("jobs/{jobId:guid}/result")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Result(Guid id, Guid jobId, ResultRequest request, CancellationToken ct)
    {
        await Authenticate(id, ct);
        var job = await Leased(id, jobId, request.LeaseId, ct);
        var now = DateTime.UtcNow;
        if (request.Success)
        {
            var label = request.Label?.Trim();
            if (string.IsNullOrWhiteSpace(label) || label.Length > 80 || request.Confidence is null or < 0 or > 1 ||
                request.DurationMs is null or < 0 or > 600000 || request.Evidence is null || (label == "no_detection" && request.Confidence != 0))
                throw AppException.BadRequest("Invalid AI result.");
            var labels = JsonSerializer.Deserialize<string[]>(job.DetectionClassesJson) ?? [];
            if (label != "no_detection" && (request.Confidence < job.ConfidenceThreshold || (labels.Length > 0 && !labels.Contains(label))))
                throw AppException.BadRequest("Detection outside configured classes or threshold.");
            var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == job.MediaId, ct) ?? throw AppException.NotFound("Media");
            db.AiDetections.Add(new AiDetection { AiServiceId = id, InferenceJobId = job.Id, ModelVersion = job.ModelVersion,
                MediaId = job.MediaId, DeviceId = job.DeviceId, BoxId = job.BoxId, CrabId = job.CrabId, FarmingAreaId = job.FarmingAreaId,
                DetectionType = label, Confidence = request.Confidence.Value, DetectedAt = now, Status = "completed",
                ResultJson = JsonSerializer.Serialize(new { source = "model", isModelInference = true, capturedMediaCreatedAt = media.CreatedAt, evidence = request.Evidence }) });
            job.Status = "Succeeded"; job.DurationMs = request.DurationMs; job.Error = null;
        }
        else { job.Status = "Failed"; job.Error = Limit(request.Error, 500) ?? "Inference failed."; }
        job.CompletedAt = now; job.LeaseId = null; job.LeaseUntil = null;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Job lease changed."); }
        return Ok(ApiResponse.Ok());
    }

    private static string? Limit(string? value, int max) => value is null ? null : value[..Math.Min(value.Length, max)];
}
