using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Application.Services;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;
using CrabSenseBE.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CrabSenseBE.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.FarmWrite)]
[Route("api/ai/inference/jobs")]
public sealed class AiInferenceController(AppDbContext db, IMediaService mediaService, IUnitOfWork uow, ICurrentUserService currentUser) : ControllerBase
{
    private readonly FarmAreaScope scope = new(uow, currentUser);
    public record QueueRequest(Guid MediaId, Guid? AiServiceId = null);

    [HttpPost]
    public async Task<IActionResult> Queue(QueueRequest request, CancellationToken ct)
    {
        // Reuse the existing media authorization boundary, including all linked entities.
        await mediaService.GetByIdAsync(request.MediaId, ct);
        var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.MediaId, ct) ?? throw AppException.NotFound("Media");
        if (media.Category is not ("image" or "video") || media.SizeBytes <= 0 || media.SizeBytes > 100L * 1024 * 1024 ||
            media.DeviceId is not Guid deviceId || media.BoxId is not Guid boxId)
            throw AppException.BadRequest("Cần media ảnh/video <=100 MB có camera và hộp nguồn.");
        var candidates = await (from assignment in db.AiCameraAssignments.AsNoTracking()
                                join serviceRow in db.AiManagedServices.AsNoTracking() on assignment.AiServiceId equals serviceRow.Id
                                where assignment.DeviceId == deviceId && serviceRow.IsEnabled
                                select serviceRow).ToListAsync(ct);
        var eligible = candidates.Where(x => AiRuntimeState.Health(x, DateTime.UtcNow) == "Online" && !x.Status.EndsWith("Requested") &&
            (!request.AiServiceId.HasValue || request.AiServiceId.Value == x.Id)).ToList();
        if (eligible.Count != 1) throw AppException.Conflict("Cần đúng một dịch vụ AI sẵn sàng cho camera, hoặc chọn rõ AiServiceId.");
        var service = eligible[0]; var id = service.Id;
        var model = await db.AiModelVersions.AsNoTracking().FirstOrDefaultAsync(x => x.AiServiceId == id && x.Version == service.Version, ct) ?? throw AppException.Conflict("Model chưa đăng ký.");
        var box = await db.Boxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == boxId, ct) ?? throw AppException.NotFound("Box");
        var row = await db.FarmingRows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == box.FarmingRowId, ct) ?? throw AppException.NotFound("Farming row");
        await scope.EnsureAsync(row.FarmingAreaId, manage: false, ct);
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deviceId, ct) ?? throw AppException.NotFound("Camera");
        if ((device.FarmingRowId.HasValue && device.FarmingRowId != row.Id) ||
            (device.FarmingAreaId.HasValue && device.FarmingAreaId != row.FarmingAreaId) ||
            (!device.FarmingRowId.HasValue && !device.FarmingAreaId.HasValue))
            throw AppException.BadRequest("Camera không thuộc vị trí của hộp.");
        var existing = await db.AiInferenceJobs.AsNoTracking().FirstOrDefaultAsync(x => x.AiServiceId == id && x.MediaId == media.Id && x.ModelVersionId == model.Id, ct);
        if (existing is not null) { await scope.EnsureAsync(existing.FarmingAreaId, manage: false, ct); return Ok(ApiResponse<object>.Ok(View(existing), "Yêu cầu đã tồn tại.")); }
        var job = new AiInferenceJob { AiServiceId = id, ModelVersionId = model.Id, ModelVersion = model.Version,
            MediaId = media.Id, DeviceId = deviceId, BoxId = boxId, CrabId = media.CrabId, FarmingAreaId = row.FarmingAreaId,
            ConfidenceThreshold = service.ConfidenceThreshold, FrameSamplingFps = service.FrameSamplingFps,
            DetectionClassesJson = service.DetectionClassesJson };
        db.AiInferenceJobs.Add(job);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw AppException.Conflict("Không thể tạo job; tải lại để kiểm tra yêu cầu trùng."); }
        return Ok(ApiResponse<object>.Ok(View(job)));
    }


    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
    {
        var job = await db.AiInferenceJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId, ct) ?? throw AppException.NotFound("AI job");
        await scope.EnsureAsync(job.FarmingAreaId, manage: false, ct);
        var detection = await db.AiDetections.AsNoTracking().Where(x => x.InferenceJobId == jobId)
            .Select(x => new { x.Id, x.DetectionType, x.Confidence, x.ModelVersion, x.DetectedAt, x.ResultJson }).FirstOrDefaultAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { job = View(job), detection }));
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? boxId, [FromQuery] Guid? farmingAreaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var allowed = await scope.SelectionAsync(farmingAreaId, ct);
        var q = db.AiInferenceJobs.AsNoTracking().AsQueryable();
        if (allowed is not null) q = q.Where(x => allowed.Contains(x.FarmingAreaId));
        if (boxId.HasValue) q = q.Where(x => x.BoxId == boxId);
        page = Math.Clamp(page, 1, 100000); pageSize = Math.Clamp(pageSize, 1, 100);
        var totalCount = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { items = rows.Select(View), totalCount, page, pageSize }));
    }

    private static object View(AiInferenceJob x) => new { x.Id, x.AiServiceId, x.MediaId, x.DeviceId, x.BoxId, x.ModelVersion, x.Status, x.Attempts, x.CreatedAt, x.CompletedAt, x.DurationMs,
        Error = x.Error == null ? null : "Inference failed; contact system administrator." };
}
