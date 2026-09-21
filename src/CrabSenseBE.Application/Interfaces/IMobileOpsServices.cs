using CrabSenseBE.Application.Common;
using CrabSenseBE.Application.DTOs.Farm;
using CrabSenseBE.Application.DTOs.Ops;

namespace CrabSenseBE.Application.Interfaces;

public interface IBoxDetailService
{
    Task<ApiResponse<BoxDetailDto>> GetDetailAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<BoxCrabItemDto>>> GetCrabsAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<BoxVideoItemDto>>> GetVideosAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<object>> ResolveQrAsync(string code, CancellationToken ct = default);

    /// <summary>Scan QR → enriched quick-result for Mobile Scan tab (single round-trip).</summary>
    Task<ApiResponse<BoxQrQuickResultDto>> GetQuickResultByQrAsync(
        string code, CancellationToken ct = default);

    Task<ApiResponse<BoxCrabItemDto>> AddCrabAsync(Guid boxId, MobileAddCrabRequest req, CancellationToken ct = default);
}

public interface IFarmOperationService
{
    Task<ApiResponse<IEnumerable<FeedingHistoryDayDto>>> GetFeedingHistoryAsync(
        int days = 7, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListAllAsync(
        int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListByBoxAsync(
        Guid boxId, int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<FarmOperationDto>>> ListByCrabAsync(
        Guid crabId, int page = 1, int limit = 50, string? type = null,
        DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<ApiResponse<FarmOperationDto>> CreateAsync(CreateFarmOperationRequest req, CancellationToken ct = default);
    Task<ApiResponse<FarmOperationDto>> UpdateAsync(Guid id, UpdateFarmOperationRequest req, CancellationToken ct = default);
}

public interface IManualInspectionService
{
    Task<ApiResponse<ManualInspectionDto>> SubmitAsync(SubmitManualInspectionRequest req, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<ManualInspectionDto>>> ListByBoxAsync(Guid boxId, CancellationToken ct = default);
    Task<ApiResponse<ManualInspectionDto>> GetByIdAsync(Guid id, CancellationToken ct = default);
}

public interface IAiOpsService
{
    Task<ApiResponse<AiDetectionDto>> AnalyzeAsync(AiAnalyzeRequest req, CancellationToken ct = default);
    Task<ApiResponse<IEnumerable<AiDetectionDto>>> ListDetectionsAsync(
        Guid? boxId = null, Guid? mediaId = null, CancellationToken ct = default);
    /// <summary>Lọc theo khu (qua hộp→dãy→khu hoặc camera) và giới hạn số bản ghi mới nhất.</summary>
    Task<ApiResponse<IEnumerable<AiDetectionDto>>> ListDetectionsAsync(
        Guid? boxId, Guid? mediaId, Guid? farmingAreaId, int? take, CancellationToken ct = default);
    Task<ApiResponse<object>> SubmitFeedbackAsync(AiFeedbackRequest req, Guid userId, CancellationToken ct = default);
}
