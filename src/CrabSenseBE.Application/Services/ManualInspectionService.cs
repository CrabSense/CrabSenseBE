using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Ops;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class ManualInspectionService : IManualInspectionService
{
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService? _currentUser;
    private readonly FarmAreaScope _areas;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ManualInspectionService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _currentUser = currentUser;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<ManualInspectionDto>> SubmitAsync(
        SubmitManualInspectionRequest req, CancellationToken ct = default)
    {
        if (req.BoxId == Guid.Empty)
            throw AppException.BadRequest("BoxId is required.");
        _ = await _uow.Boxes.GetByIdAsync(req.BoxId, ct) ?? throw AppException.NotFound("Box");
        await _areas.EnsureBoxAsync(req.BoxId, manage: false, ct);

        var actor = await _areas.CurrentUserAsync(ct);
        if (req.Weight is <= 0) throw AppException.BadRequest("Trọng lượng kiểm tra phải lớn hơn 0.");
        if (req.Timestamp?.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
            throw AppException.BadRequest("Thời điểm kiểm tra không được ở tương lai.");
        // Evidence must be a registered media asset for this exact box.
        var guard = new MediaAssetAreaGuard(_uow, _currentUser);
        if (req.RelatedVideoId is Guid videoId)
        {
            var video = await _uow.MediaAssets.GetByIdAsync(videoId, ct)
                ?? throw AppException.NotFound("MediaAsset");
            await guard.EnsureAsync(video, ct);
            if (!string.Equals(video.Category, "video", StringComparison.OrdinalIgnoreCase)
                || video.BoxId != req.BoxId)
                throw AppException.Forbidden("Video không thuộc hộp kiểm tra.");
        }
        foreach (var photo in req.PhotoUrls ?? Array.Empty<string>())
        {
            MediaAsset? asset;
            if (MediaReferenceGuard.TryParseContentId(photo, out var mediaId))
                asset = await _uow.MediaAssets.GetByIdAsync(mediaId, ct);
            else
                asset = await _uow.MediaAssets.FirstOrDefaultAsync(m =>
                    m.ShareLink == photo || m.WebViewLink == photo || m.WebContentLink == photo, ct);
            if (asset is null || asset.BoxId != req.BoxId
                || !string.Equals(asset.Category, "image", StringComparison.OrdinalIgnoreCase))
                throw AppException.Forbidden("Ảnh không thuộc hộp kiểm tra hoặc chưa đăng ký media.");
            await guard.EnsureAsync(asset, ct);
        }

        var isUpdate = Guid.TryParse(req.Id, out var existingId) && existingId != Guid.Empty;
        if (isUpdate)
            throw AppException.Conflict(
                "Bản kiểm tra đã ghi nhận không được sửa trực tiếp. Hãy tạo bản ghi bổ sung.");
        var entity = new Inspection { Id = Guid.NewGuid() };
        await _uow.Inspections.AddAsync(entity, ct);

        entity.BoxId = req.BoxId;
        entity.RelatedMediaId = req.RelatedVideoId;
        entity.MoltingStatus = req.MoltingStatus ?? "hardShell";
        entity.HealthStatus = req.HealthStatus ?? "unknown";
        entity.WeightGram = req.Weight;
        entity.Notes = req.Notes;
        entity.PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls ?? Array.Empty<string>(), JsonOpts);
        entity.InspectedAt = req.Timestamp ?? DateTime.UtcNow;
        entity.InspectorId = _currentUser is { IsAuthenticated: true }
            ? _currentUser.UserId
            : req.OperatorId ?? Guid.Empty;
        entity.OperatorName = actor is null ? "Operator" : string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username : actor.FullName;
        entity.AiAgreement = req.AiAgreement;
        entity.InspectionType = "manual";
        entity.Result = req.HealthStatus ?? "unknown";
        entity.Score = req.Weight;
        entity.UpdatedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<ManualInspectionDto>.Ok(Map(entity), "Submitted.");
    }

    public async Task<ApiResponse<IEnumerable<ManualInspectionDto>>> ListByBoxAsync(
        Guid boxId, CancellationToken ct = default)
    {
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        var list = (await _uow.Inspections.FindAsync(i => i.BoxId == boxId, ct))
            .OrderByDescending(i => i.InspectedAt)
            .Select(Map)
            .ToList();
        return ApiResponse<IEnumerable<ManualInspectionDto>>.Ok(list);
    }

    public async Task<ApiResponse<ManualInspectionDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _uow.Inspections.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("Inspection");
        if (entity.BoxId is not Guid boxId)
            throw AppException.Forbidden("Bản kiểm tra chưa xác định hộp.");
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        return ApiResponse<ManualInspectionDto>.Ok(Map(entity));
    }

    private static ManualInspectionDto Map(Inspection i)
    {
        IReadOnlyList<string> photos = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(i.PhotoUrlsJson))
        {
            try { photos = JsonSerializer.Deserialize<List<string>>(i.PhotoUrlsJson) ?? new List<string>(); }
            catch { /* ignore */ }
        }

        return new ManualInspectionDto(
            i.Id,
            i.BoxId ?? Guid.Empty,
            i.RelatedMediaId,
            i.MoltingStatus ?? "hardShell",
            i.HealthStatus ?? "unknown",
            i.WeightGram ?? 0,
            i.Notes ?? "",
            photos,
            i.InspectedAt,
            i.InspectorId,
            i.OperatorName ?? "",
            i.AiAgreement);
    }
}
