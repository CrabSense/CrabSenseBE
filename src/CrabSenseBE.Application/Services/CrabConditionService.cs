using System.Text.Json;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Condition;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class CrabConditionService : ICrabConditionService
{
    public const string InspectionTypeCondition = "condition";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IUnitOfWork _uow;

    public CrabConditionService(IUnitOfWork uow) => _uow = uow;

    public Task<ApiResponse<CrabConditionDto>> EvaluateAsync(
        EvaluateCrabConditionRequest req, CancellationToken ct = default)
    {
        try
        {
            var dto = KnConditionEngine.Evaluate(req);
            return Task.FromResult(ApiResponse<CrabConditionDto>.Ok(dto));
        }
        catch (ArgumentException ex)
        {
            throw AppException.BadRequest(ex.Message);
        }
    }

    public Task<ApiResponse<IReadOnlyList<CrabConditionDto>>> EvaluateBatchAsync(
        EvaluateCrabConditionBatchRequest req, CancellationToken ct = default)
    {
        if (req.Items is null || req.Items.Count == 0)
            throw AppException.BadRequest("Items is required.");

        try
        {
            var list = req.Items.Select(KnConditionEngine.Evaluate).ToList();
            return Task.FromResult(ApiResponse<IReadOnlyList<CrabConditionDto>>.Ok(list));
        }
        catch (ArgumentException ex)
        {
            throw AppException.BadRequest(ex.Message);
        }
    }

    public async Task<ApiResponse<CrabConditionDto>> SubmitAsync(
        SubmitCrabConditionRequest req, CancellationToken ct = default)
    {
        if (req.BoxId == Guid.Empty)
            throw AppException.BadRequest("BoxId is required.");

        _ = await _uow.Boxes.GetByIdAsync(req.BoxId, ct) ?? throw AppException.NotFound("Box");

        CrabConditionDto eval;
        try
        {
            eval = KnConditionEngine.Evaluate(new EvaluateCrabConditionRequest(
                req.WeightG,
                req.CarapaceWidthCm,
                req.CarapaceLengthCm,
                req.CrabType,
                BoxId: req.BoxId,
                CrabId: req.CrabId,
                Dimension: req.Dimension));
        }
        catch (ArgumentException ex)
        {
            throw AppException.BadRequest(ex.Message);
        }

        if (req.CrabId is Guid crabId && crabId != Guid.Empty)
        {
            var crab = await _uow.Crabs.GetByIdAsync(crabId, ct) ?? throw AppException.NotFound("Crab");
            if (req.UpdateCrabRecord)
            {
                crab.WeightGram = req.WeightG;
                crab.MoltingStage = eval.MoltingStatusHint ?? crab.MoltingStage;
                var oldCondition = crab.Condition;
                var oldStatus = crab.Status;
                crab.Condition = CrabConditions.FromMoltingAndStatus(
                    crab.MoltingStage,
                    eval.Status == "pre_molt" ? CrabStatus.Molting : crab.Status);
                if (eval.Status == "pre_molt")
                    crab.Status = CrabStatus.Molting;
                else
                    crab.Status = CrabConditions.ToLifecycle(crab.Condition);
                crab.AiPrediction = eval.MoltingStatusHint ?? eval.Status;
                crab.UpdatedAt = DateTime.UtcNow;
                _uow.Crabs.Update(crab);

                if (oldCondition != crab.Condition || oldStatus != crab.Status)
                {
                    await _uow.CrabStatusHistories.AddAsync(new CrabStatusHistory
                    {
                        CrabId = crab.Id,
                        OldCondition = oldCondition,
                        NewCondition = crab.Condition,
                        OldStatus = oldStatus,
                        NewStatus = crab.Status,
                        Source = "ai",
                        Reason = "Condition evaluate"
                    }, ct);
                }

                await _uow.CrabWeightHistories.AddAsync(new CrabWeightHistory
                {
                    CrabId = crab.Id,
                    WeightGram = req.WeightG,
                    Source = "ai",
                    Notes = "Condition evaluate"
                }, ct);

                await _uow.CrabAiAnalyses.AddAsync(new CrabAiAnalysis
                {
                    CrabId = crab.Id,
                    BoxId = req.BoxId,
                    Prediction = crab.AiPrediction ?? "condition",
                    Confidence = 0,
                    AnalyzedAt = DateTime.UtcNow,
                    ModelVersion = "kn-condition"
                }, ct);
            }
        }

        var payload = new
        {
            kn = eval.Kn,
            status = eval.Status,
            weightEstimatedG = eval.WeightEstimatedG,
            carapaceWidthCm = eval.CarapaceWidthCm,
            carapaceLengthCm = eval.CarapaceLengthCm,
            profileUsed = eval.ProfileUsed,
            dimensionUsed = eval.DimensionUsed,
            meatEstimatedG = eval.MeatEstimatedG,
            recommendation = eval.Recommendation,
            alertHarvest = eval.AlertHarvest,
            crabType = KnConditionEngine.NormalizeType(req.CrabType),
            checklist = new
            {
                isSoftShell = req.IsSoftShell,
                hasGoodReflex = req.HasGoodReflex,
                hasDoubleLine = req.HasDoubleLine,
                isMolted = req.IsMolted
            },
            notes = req.Notes
        };

        var entity = new Inspection
        {
            Id = Guid.NewGuid(),
            BoxId = req.BoxId,
            CrabId = req.CrabId,
            InspectionType = InspectionTypeCondition,
            Result = eval.Status,
            Score = eval.Kn,
            WeightGram = req.WeightG,
            MoltingStatus = eval.MoltingStatusHint,
            HealthStatus = eval.Status is "lean" or "post_molt" ? "stress" : "normal",
            Notes = JsonSerializer.Serialize(payload, JsonOpts),
            PhotoUrlsJson = JsonSerializer.Serialize(req.PhotoUrls ?? Array.Empty<string>(), JsonOpts),
            InspectedAt = DateTime.UtcNow,
            InspectorId = req.OperatorId ?? Guid.Empty,
            OperatorName = req.OperatorName ?? "Operator"
        };
        await _uow.Inspections.AddAsync(entity, ct);

        if (eval.AlertHarvest)
        {
            await _uow.AiRecommendations.AddAsync(new AiRecommendation
            {
                Id = Guid.NewGuid(),
                Category = "harvest",
                Recommendation = eval.Recommendation,
                Priority = 1,
                RelatedEntityId = req.BoxId,
                RelatedEntityType = "box",
                IsActedUpon = false
            }, ct);
        }

        await _uow.SaveChangesAsync(ct);

        return ApiResponse<CrabConditionDto>.Ok(eval with
        {
            Id = entity.Id,
            RecordedAt = entity.InspectedAt
        }, "Condition recorded.");
    }

    public async Task<ApiResponse<IEnumerable<CrabConditionDto>>> ListByBoxAsync(
        Guid boxId, CancellationToken ct = default)
    {
        var list = (await _uow.Inspections.FindAsync(
                i => i.BoxId == boxId && i.InspectionType == InspectionTypeCondition, ct))
            .OrderByDescending(i => i.InspectedAt)
            .Select(MapInspection)
            .ToList();
        return ApiResponse<IEnumerable<CrabConditionDto>>.Ok(list);
    }

    public async Task<ApiResponse<CrabConditionDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _uow.Inspections.GetByIdAsync(id, ct)
            ?? throw AppException.NotFound("CrabCondition");
        if (!string.Equals(entity.InspectionType, InspectionTypeCondition, StringComparison.OrdinalIgnoreCase))
            throw AppException.NotFound("CrabCondition");
        return ApiResponse<CrabConditionDto>.Ok(MapInspection(entity));
    }

    public Task<ApiResponse<DailyCheckDto>> DailyCheckAsync(
        DailyCheckRequest req, CancellationToken ct = default)
    {
        var dto = KnConditionEngine.DailyCheck(req);
        return Task.FromResult(ApiResponse<DailyCheckDto>.Ok(dto));
    }

    private static CrabConditionDto MapInspection(Inspection i)
    {
        decimal? cw = null, cl = null, west = null, meat = null;
        string profile = "stored", dim = "stored", recommendation = i.Notes ?? "";
        var alert = false;

        if (!string.IsNullOrWhiteSpace(i.Notes))
        {
            try
            {
                using var doc = JsonDocument.Parse(i.Notes);
                var root = doc.RootElement;
                if (root.TryGetProperty("carapaceWidthCm", out var pCw) && pCw.ValueKind == JsonValueKind.Number)
                    cw = pCw.GetDecimal();
                if (root.TryGetProperty("carapaceLengthCm", out var pCl) && pCl.ValueKind == JsonValueKind.Number)
                    cl = pCl.GetDecimal();
                if (root.TryGetProperty("weightEstimatedG", out var pWest) && pWest.ValueKind == JsonValueKind.Number)
                    west = pWest.GetDecimal();
                if (root.TryGetProperty("meatEstimatedG", out var pMeat) && pMeat.ValueKind == JsonValueKind.Number)
                    meat = pMeat.GetDecimal();
                if (root.TryGetProperty("profileUsed", out var pProf))
                    profile = pProf.GetString() ?? profile;
                if (root.TryGetProperty("dimensionUsed", out var pDim))
                    dim = pDim.GetString() ?? dim;
                if (root.TryGetProperty("recommendation", out var pRec))
                    recommendation = pRec.GetString() ?? recommendation;
                if (root.TryGetProperty("alertHarvest", out var pAlert))
                    alert = pAlert.GetBoolean();
            }
            catch
            {
                /* legacy plain notes */
            }
        }

        return new CrabConditionDto(
            i.Id,
            i.BoxId,
            null,
            i.CrabId,
            i.WeightGram ?? 0,
            cw,
            cl,
            west ?? 0,
            i.Score ?? 0,
            i.Result,
            recommendation,
            alert,
            profile,
            dim,
            meat,
            i.MoltingStatus,
            i.InspectedAt);
    }
}
