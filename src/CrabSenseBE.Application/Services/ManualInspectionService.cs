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
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ManualInspectionService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<ManualInspectionDto>> SubmitAsync(
        SubmitManualInspectionRequest req, CancellationToken ct = default)
    {
        if (req.BoxId == Guid.Empty)
            throw AppException.BadRequest("BoxId is required.");
        _ = await _uow.Boxes.GetByIdAsync(req.BoxId, ct) ?? throw AppException.NotFound("Box");

        Inspection entity;
        var isUpdate = Guid.TryParse(req.Id, out var existingId) && existingId != Guid.Empty;
        if (isUpdate)
        {
            entity = await _uow.Inspections.GetByIdAsync(existingId, ct)
                ?? throw AppException.NotFound("Inspection");
        }
        else
        {
            entity = new Inspection { Id = Guid.NewGuid() };
            await _uow.Inspections.AddAsync(entity, ct);
        }

        entity.BoxId = req.BoxId;
        entity.RelatedMediaId = req.RelatedVideoId;
        entity.MoltingStatus = req.MoltingStatus ?? "hardShell";
        entity.HealthStatus = req.HealthStatus ?? "unknown";
        entity.WeightGram = req.Weight;
        entity.Notes = req.Notes;
        entity.PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls ?? Array.Empty<string>(), JsonOpts);
        entity.InspectedAt = req.Timestamp ?? DateTime.UtcNow;
        entity.InspectorId = req.OperatorId ?? Guid.Empty;
        entity.OperatorName = req.OperatorName ?? "Operator";
        entity.AiAgreement = req.AiAgreement;
        entity.InspectionType = "manual";
        entity.Result = req.HealthStatus ?? "unknown";
        entity.Score = req.Weight;
        entity.UpdatedAt = DateTime.UtcNow;

        if (isUpdate)
            _uow.Inspections.Update(entity);

        await _uow.SaveChangesAsync(ct);
        return ApiResponse<ManualInspectionDto>.Ok(Map(entity), "Submitted.");
    }

    public async Task<ApiResponse<IEnumerable<ManualInspectionDto>>> ListByBoxAsync(
        Guid boxId, CancellationToken ct = default)
    {
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
