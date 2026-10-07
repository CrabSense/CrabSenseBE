using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.TrainingData;

namespace CrabSenseBE.Application.Interfaces;

public interface ITrainingDataService
{
    Task<ApiResponse<Guid>> CreateFeedingAsync(CreateFeedingEventRequest request, Guid? userId, CancellationToken ct);
    Task<ApiResponse<Guid>> CreateObservationAsync(CreateObservationEventRequest request, CancellationToken ct);
    Task<ApiResponse> LabelAsync(SubmitTrainingLabelRequest request, Guid? userId, CancellationToken ct);
    Task<ApiResponse<IReadOnlyList<TrainingDataDto>>> ListAsync(Guid crabId, CancellationToken ct);
}
