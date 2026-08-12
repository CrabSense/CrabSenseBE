using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Condition;

namespace CrabSenseBE.Application.Interfaces;

public interface ICrabConditionService
{
    Task<ApiResponse<CrabConditionDto>> EvaluateAsync(EvaluateCrabConditionRequest req, CancellationToken ct = default);
    Task<ApiResponse<IReadOnlyList<CrabConditionDto>>> EvaluateBatchAsync(EvaluateCrabConditionBatchRequest req, CancellationToken ct = default);
    Task<ApiResponse<CrabConditionDto>> SubmitAsync(SubmitCrabConditionRequest req, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<CrabConditionDto>>> ListByBoxAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<CrabConditionDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApiResponse<DailyCheckDto>> DailyCheckAsync(DailyCheckRequest req, CancellationToken ct = default);
}
