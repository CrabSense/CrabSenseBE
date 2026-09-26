using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.TrainingData;
using CrabSenseBE.Application.Interfaces;
using CrabSenseBE.Domain.Entities;
using CrabSenseBE.Domain.Interfaces;

namespace CrabSenseBE.Application.Services;

public class TrainingDataService : ITrainingDataService
{
    private readonly IUnitOfWork _uow;
    public TrainingDataService(IUnitOfWork uow) => _uow = uow;

    public async Task<ApiResponse<Guid>> CreateFeedingAsync(
        CreateFeedingEventRequest request, Guid? userId, CancellationToken ct)
    {
        if (request.CrabId == Guid.Empty)
            throw AppException.BadRequest("CrabId is required.");
        _ = await _uow.Crabs.GetByIdAsync(request.CrabId, ct)
            ?? throw AppException.NotFound("Crab");

        var entity = new FeedingEvent
        {
            CrabId = request.CrabId, BoxId = request.BoxId,
            FedAt = request.FedAt ?? DateTime.UtcNow,
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
        if (request.CrabId == Guid.Empty)
            throw AppException.BadRequest("CrabId is required.");
        _ = await _uow.Crabs.GetByIdAsync(request.CrabId, ct)
            ?? throw AppException.NotFound("Crab");

        var entity = new ObservationEvent
        {
            CrabId = request.CrabId, BoxId = request.BoxId,
            FeedingEventId = request.FeedingEventId,
            ObservedAt = request.ObservedAt ?? DateTime.UtcNow,
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
        if (request.FeedingEventId is null && request.ObservationEventId is null)
            throw AppException.BadRequest("FeedingEventId or ObservationEventId is required.");
        var entity = new TrainingLabel
        {
            FeedingEventId = request.FeedingEventId,
            ObservationEventId = request.ObservationEventId,
            Ate = request.Ate, ConsumptionLevel = request.ConsumptionLevel,
            MovementLevel = request.MovementLevel, FoodResponse = request.FoodResponse,
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
        var feedings = (await _uow.FeedingEvents.FindAsync(x => x.CrabId == crabId, ct))
            .OrderByDescending(x => x.FedAt).ToList();
        var observations = await _uow.ObservationEvents.FindAsync(x => x.CrabId == crabId, ct);
        var labels = await _uow.TrainingLabels.GetAllAsync(ct);
        var result = feedings.Select(feed =>
        {
            var observation = observations.Where(x => x.FeedingEventId == feed.Id)
                .OrderBy(x => x.ObservedAt).FirstOrDefault();
            var label = labels.Where(x => x.FeedingEventId == feed.Id ||
                    x.ObservationEventId == observation?.Id)
                .OrderByDescending(x => x.LabeledAt).FirstOrDefault();
            return new TrainingDataDto(feed.Id, observation?.Id, feed.CrabId, feed.BoxId,
                feed.InitialFoodGram, label?.ConsumptionLevel, label?.Ate,
                label?.MovementLevel, label?.FoodResponse, observation?.VideoMediaId,
                feed.FedAt, observation?.ObservedAt);
        }).ToList();
        return ApiResponse<IReadOnlyList<TrainingDataDto>>.Ok(result);
    }
}
