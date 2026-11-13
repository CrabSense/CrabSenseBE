using CrabSenseBE.Domain.Enums;
using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.TrainingData;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class TrainingDataService : ITrainingDataService
{
    private readonly IUnitOfWork _uow;
    private readonly FarmAreaScope _areas;
    public TrainingDataService(IUnitOfWork uow, ICurrentUserService? currentUser = null)
    {
        _uow = uow;
        _areas = new FarmAreaScope(uow, currentUser);
    }

    public async Task<ApiResponse<Guid>> CreateFeedingAsync(
        CreateFeedingEventRequest request, Guid? userId, CancellationToken ct)
    {
        var actor = await _areas.CurrentUserAsync(ct);
        if (actor is not null && (actor.Role != UserRole.FarmStaff || userId != actor.Id))
            throw AppException.Forbidden("Chỉ Farm Staff ghi nhận feeding.");
        if (request.CrabId == Guid.Empty)
            throw AppException.BadRequest("CrabId is required.");
        var crab = await _uow.Crabs.GetByIdAsync(request.CrabId, ct)
            ?? throw AppException.NotFound("Crab");
        var fedAt = request.FedAt ?? DateTime.UtcNow;
        if (request.InitialFoodGram is < 0) throw AppException.BadRequest("InitialFoodGram không được âm.");
        var boxId = await ValidateCrabBoxAtAsync(
            crab, request.BoxId, fedAt, ct);

        var entity = new FeedingEvent
        {
            CrabId = request.CrabId, BoxId = boxId,
            FedAt = fedAt,
            FoodType = request.FoodType, InitialFoodGram = request.InitialFoodGram,
            Notes = request.Notes, CreatedBy = userId
        };
        await _uow.FeedingEvents.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<Guid>.Ok(entity.Id, "Feeding event created.");
    }

    public async Task<ApiResponse<Guid>> CreateObservationAsync(
        CreateObservationEventRequest request, CancellationToken ct)
    {
        var actor = await _areas.CurrentUserAsync(ct);
        if (actor is not null && actor.Role != UserRole.FarmStaff)
            throw AppException.Forbidden("Chỉ Farm Staff ghi nhận observation.");
        if (request.CrabId == Guid.Empty)
            throw AppException.BadRequest("CrabId is required.");
        var crab = await _uow.Crabs.GetByIdAsync(request.CrabId, ct)
            ?? throw AppException.NotFound("Crab");
        var observedAt = request.ObservedAt ?? DateTime.UtcNow;
        if (request.DurationSeconds is < 0) throw AppException.BadRequest("DurationSeconds không được âm.");
        var boxId = await ValidateCrabBoxAtAsync(
            crab, request.BoxId, observedAt, ct);
        if (request.FeedingEventId is Guid feedingId)
        {
            var feeding = await _uow.FeedingEvents.GetByIdAsync(feedingId, ct)
                ?? throw AppException.NotFound("FeedingEvent");
            if (feeding.CrabId != crab.Id || feeding.BoxId != boxId || observedAt < feeding.FedAt)
                throw AppException.Conflict(
                    "FeedingEvent không thuộc cùng cua và hộp của observation.");
        }

        var entity = new ObservationEvent
        {
            CrabId = request.CrabId, BoxId = boxId,
            FeedingEventId = request.FeedingEventId,
            ObservedAt = observedAt,
            DurationSeconds = request.DurationSeconds,
            ObservationType = string.IsNullOrWhiteSpace(request.ObservationType)
                ? "AFTER_FEEDING" : request.ObservationType.Trim().ToUpperInvariant(),
            Notes = request.Notes
        };
        await _uow.ObservationEvents.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse<Guid>.Ok(entity.Id, "Observation event created.");
    }

    public async Task<ApiResponse> LabelAsync(
        SubmitTrainingLabelRequest request, Guid? userId, CancellationToken ct)
    {
        var actor = await _areas.CurrentUserAsync(ct);
        if (actor is not null && (actor.Role != UserRole.FarmManager || userId != actor.Id))
            throw AppException.Forbidden("Chỉ Farm Manager xác nhận nhãn training.");
        if ((request.FeedingEventId is null) == (request.ObservationEventId is null))
            throw AppException.BadRequest(
                "Exactly one of FeedingEventId or ObservationEventId is required.");
        if (request.FeedingEventId is Guid feedingId)
        {
            var feeding = await _uow.FeedingEvents.GetByIdAsync(feedingId, ct)
                ?? throw AppException.NotFound("FeedingEvent");
            var crab = await _uow.Crabs.GetByIdAsync(feeding.CrabId, ct)
                ?? throw AppException.NotFound("Crab");
            _ = await ValidateCrabBoxAtAsync(
                crab, feeding.BoxId, feeding.FedAt, ct);
        }
        else if (request.ObservationEventId is Guid observationId)
        {
            var observation = await _uow.ObservationEvents.GetByIdAsync(observationId, ct)
                ?? throw AppException.NotFound("ObservationEvent");
            var crab = await _uow.Crabs.GetByIdAsync(observation.CrabId, ct)
                ?? throw AppException.NotFound("Crab");
            _ = await ValidateCrabBoxAtAsync(
                crab, observation.BoxId, observation.ObservedAt, ct);
        }
        if (request.ActualWeightGram is <= 0) throw AppException.BadRequest("ActualWeightGram phải lớn hơn 0.");
        var entity = new TrainingLabel
        {
            FeedingEventId = request.FeedingEventId,
            ObservationEventId = request.ObservationEventId,
            Ate = request.Ate, ConsumptionLevel = request.ConsumptionLevel,
            MovementLevel = request.MovementLevel,
            ActualWeightGram = request.ActualWeightGram, Notes = request.Notes,
            LabeledBy = userId
        };
        await _uow.TrainingLabels.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ApiResponse.Ok("Training label saved.");
    }

    public async Task<ApiResponse<IReadOnlyList<TrainingDataDto>>> ListAsync(
        Guid crabId, CancellationToken ct)
    {
        var crab = await _uow.Crabs.GetByIdAsync(crabId, ct)
            ?? throw AppException.NotFound("Crab");
        var feedings = (await _uow.FeedingEvents.FindAsync(x => x.CrabId == crabId, ct))
            .OrderByDescending(x => x.FedAt).ToList();
        var observations = (await _uow.ObservationEvents.FindAsync(
                x => x.CrabId == crabId, ct))
            .ToList();
        foreach (var boxId in feedings.Select(item => item.BoxId)
                     .Concat(observations.Select(item => item.BoxId))
                     .Where(id => id.HasValue)
                     .Select(id => id!.Value)
                     .Distinct())
            await _areas.EnsureBoxAsync(boxId, manage: false, ct);

        var feedingIds = feedings.Select(item => item.Id).ToHashSet();
        var observationIds = observations.Select(item => item.Id).ToHashSet();
        var labels = feedingIds.Count == 0 && observationIds.Count == 0
            ? []
            : (await _uow.TrainingLabels.FindAsync(
                label => (label.FeedingEventId != null
                          && feedingIds.Contains(label.FeedingEventId.Value))
                         || (label.ObservationEventId != null
                             && observationIds.Contains(
                                 label.ObservationEventId.Value)),
                ct)).ToList();
        var result = feedings.Select(feed =>
        {
            var observation = observations.Where(x => x.FeedingEventId == feed.Id)
                .OrderBy(x => x.ObservedAt).FirstOrDefault();
            var label = labels.Where(x =>
                    x.FeedingEventId == feed.Id
                    || (observation is not null
                        && x.ObservationEventId == observation.Id))
                .OrderByDescending(x => x.LabeledAt).FirstOrDefault();
            return new TrainingDataDto(feed.Id, observation?.Id, feed.CrabId, feed.BoxId,
                feed.InitialFoodGram, label?.ConsumptionLevel, label?.Ate,
                label?.MovementLevel, observation?.VideoMediaId,
                feed.FedAt, observation?.ObservedAt);
        }).ToList();
        return ApiResponse<IReadOnlyList<TrainingDataDto>>.Ok(result);
    }

    private async Task<Guid> ValidateCrabBoxAtAsync(
        Crab crab,
        Guid? requestedBoxId,
        DateTime eventAt,
        CancellationToken ct)
    {
        var effectiveAt = eventAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(eventAt, DateTimeKind.Utc)
            : eventAt.ToUniversalTime();
        if (effectiveAt > DateTime.UtcNow.AddMinutes(5) || effectiveAt < crab.StockedAt)
            throw AppException.BadRequest("Thời điểm sự kiện không hợp lệ.");
        Guid? evidenceBoxId;
        if (effectiveAt >= DateTime.UtcNow.AddMinutes(-5))
        {
            evidenceBoxId = crab.BoxId;
        }
        else
        {
            var allocations = await _uow.CrabBoxAllocations.FindAsync(
                allocation => allocation.CrabId == crab.Id
                    && allocation.StartTime <= effectiveAt
                    && (allocation.EndTime == null
                        || allocation.EndTime >= effectiveAt),
                ct) ?? [];
            evidenceBoxId = allocations
                .OrderByDescending(item => item.StartTime)
                .Select(item => (Guid?)item.BoxId)
                .FirstOrDefault();
        }
        if (evidenceBoxId is not Guid boxId || boxId == Guid.Empty)
            throw AppException.Conflict(
                "Không có lịch sử Box hợp lệ tại thời điểm sự kiện.");
        if (requestedBoxId.HasValue && requestedBoxId.Value != boxId)
            throw AppException.Conflict(
                "BoxId không khớp lịch sử vị trí cua tại thời điểm sự kiện.");
        await _areas.EnsureBoxAsync(boxId, manage: false, ct);
        return boxId;
    }
}
